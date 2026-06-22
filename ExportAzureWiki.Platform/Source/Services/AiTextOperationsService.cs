using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Services;

public sealed class AiTextOperationsService
{
    private const int AssumedModelContextTokens = 128000;
    private const string SystemPrompt = "You are a technical writing assistant. Keep structure, preserve markdown, be concise and precise.";

    private readonly AiProviderService _providerService;
    private readonly HttpClient _httpClient = new();

    public AiTextOperationsService(AiProviderService providerService)
    {
        _providerService = providerService;
    }

    public async Task<string> GenerateSummaryAsync(string sourceContent, CancellationToken cancellationToken = default)
    {
        var provider = await RequireProviderAsync(cancellationToken).ConfigureAwait(false);
        var userPrompt = BuildSummaryPrompt(sourceContent);
        return await ExecuteChatAsync(provider, userPrompt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GenerateSummaryFromSummariesAsync(string summariesContent, CancellationToken cancellationToken = default)
    {
        var provider = await RequireProviderAsync(cancellationToken).ConfigureAwait(false);
        var userPrompt = BuildSummaryFromSummariesPrompt(summariesContent);
        return await ExecuteChatAsync(provider, userPrompt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GenerateIndexAsync(string sourceContent, CancellationToken cancellationToken = default)
    {
        var provider = await RequireProviderAsync(cancellationToken).ConfigureAwait(false);
        var userPrompt = BuildIndexPrompt(sourceContent);
        return await ExecuteChatAsync(provider, userPrompt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GenerateQuizAsync(
        string sourceContent,
        int directQuestions,
        int multipleChoiceQuestions,
        CancellationToken cancellationToken = default)
    {
        var provider = await RequireProviderAsync(cancellationToken).ConfigureAwait(false);
        var userPrompt = BuildQuizPrompt(sourceContent, directQuestions, multipleChoiceQuestions);
        return await ExecuteChatAsync(provider, userPrompt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AnswerQuestionAsync(
        string question,
        string sourceContent,
        CancellationToken cancellationToken = default)
    {
        var provider = await RequireProviderAsync(cancellationToken).ConfigureAwait(false);
        var userPrompt = BuildAnswerPrompt(question, sourceContent);
        return await ExecuteChatAsync(provider, userPrompt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the models the given provider exposes (OpenAI-compatible
    /// <c>/v1/models</c>, or Azure deployments). Operates on the supplied
    /// provider (the one being edited), not the configured default.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(AiProvider provider, CancellationToken cancellationToken = default)
    {
        if (provider == null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        var endpoint = BuildModelsEndpoint(provider);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
        ApplyHeaders(httpRequest, provider, IsAzureProvider(provider));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var mapped = TryMapKnownProviderError((int)response.StatusCode, responseContent);
            throw new InvalidOperationException(!string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : LocalizationManager.Sf("ai.runtime.error.http_status", (int)response.StatusCode, TrimForLog(responseContent, 500)));
        }

        return ParseModelIds(responseContent);
    }

    /// <summary>
    /// Lightweight connection test: lists the provider's models, which exercises
    /// the endpoint URL and authentication without consuming generation tokens.
    /// </summary>
    public async Task<(bool Success, string Message, IReadOnlyList<string> Models)> TestConnectionAsync(
        AiProvider provider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await ListModelsAsync(provider, cancellationToken).ConfigureAwait(false);
            return (true,
                LocalizationManager.Sf("ai.runtime.test.ok", models.Count),
                models);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, []);
        }
    }

    internal static string BuildModelsEndpoint(AiProvider provider)
    {
        var endpoint = (provider.EndpointUrl ?? string.Empty).Trim();

        if (IsAzureProvider(provider))
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException(
                    LocalizationManager.S("ai.runtime.error.missing_endpoint_azure",
                        "Para Azure OpenAI, configure o endpoint base do recurso."));
            }

            var apiVersion = string.IsNullOrWhiteSpace(provider.ApiVersion) ? "2024-10-21" : provider.ApiVersion.Trim();
            return $"{endpoint.TrimEnd('/')}/openai/deployments?api-version={Uri.EscapeDataString(apiVersion)}";
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "https://api.openai.com/v1/models";
        }

        if (endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint.Replace("/chat/completions", "/models", StringComparison.OrdinalIgnoreCase);
        }

        return $"{endpoint.TrimEnd('/')}/v1/models";
    }

    /// <summary>
    /// Parses a models-listing response into a sorted list of model ids. Handles
    /// the OpenAI/Azure <c>{ "data": [ { "id": ... } ] }</c> shape and a bare
    /// array fallback.
    /// </summary>
    internal static IReadOnlyList<string> ParseModelIds(string json)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return ids;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement array;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            array = data;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else
        {
            return ids;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = item.TryGetProperty("id", out var idNode) && idNode.ValueKind == JsonValueKind.String
                ? idNode.GetString()
                : item.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String
                    ? nameNode.GetString()
                    : null;

            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id!);
            }
        }

        return ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<AiProvider> RequireProviderAsync(CancellationToken cancellationToken)
    {
        var provider = await _providerService.GetDefaultActiveProviderAsync().ConfigureAwait(false);
        if (provider == null)
        {
            throw new InvalidOperationException(
                LocalizationManager.S("ai.runtime.error.no_provider",
                    "Nenhum provedor de IA ativo foi encontrado. Configure um provedor em Segurança > Configurar IA."));
        }

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new InvalidOperationException(
                LocalizationManager.S("ai.runtime.error.missing_api_key",
                    "O provedor de IA selecionado não possui API Key."));
        }

        if (string.IsNullOrWhiteSpace(provider.ModelName))
        {
            throw new InvalidOperationException(
                LocalizationManager.S("ai.runtime.error.missing_model",
                    "O provedor de IA selecionado não possui modelo configurado."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return provider;
    }

    private async Task<string> ExecuteChatAsync(
        AiProvider provider,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(provider);
        var providerIsAzure = IsAzureProvider(provider);
        var runtimeOptions = ProviderRuntimeOptions.Parse(provider.ConfigurationJson);
        ValidatePromptSize(userPrompt, runtimeOptions.MaxTokens);
        var request = BuildPayload(provider, userPrompt);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(request, Encoding.UTF8, "application/json")
        };

        ApplyHeaders(httpRequest, provider, providerIsAzure);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtimeOptions.TimeoutSeconds));

        LoggingService.LogInfo($"AI_EXEC: provider={provider.ProviderName}; model={provider.ModelName}; endpoint={endpoint}; timeout={runtimeOptions.TimeoutSeconds}s");
        using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var mappedMessage = TryMapKnownProviderError(status, responseContent);
            if (!string.IsNullOrWhiteSpace(mappedMessage))
            {
                throw new InvalidOperationException(mappedMessage);
            }

            throw new InvalidOperationException(
                LocalizationManager.Sf("ai.runtime.error.http_status",
                    status, TrimForLog(responseContent, 500)));
        }

        return ExtractContent(responseContent);
    }

    private static string BuildPayload(AiProvider provider, string userPrompt)
    {
        var options = ProviderRuntimeOptions.Parse(provider.ConfigurationJson);
        var body = new Dictionary<string, object?>
        {
            ["model"] = provider.ModelName,
            ["messages"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["role"] = "system",
                    ["content"] = SystemPrompt
                },
                new Dictionary<string, string>
                {
                    ["role"] = "user",
                    ["content"] = userPrompt
                }
            },
            ["temperature"] = options.Temperature,
            ["max_tokens"] = options.MaxTokens
        };

        if (options.TopP.HasValue)
        {
            body["top_p"] = options.TopP.Value;
        }

        return JsonSerializer.Serialize(body);
    }

    private static void ApplyHeaders(HttpRequestMessage request, AiProvider provider, bool providerIsAzure)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (providerIsAzure)
        {
            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                request.Headers.Add("api-key", provider.ApiKey);
            }
            return;
        }

        // Local servers (Ollama/LM Studio) accept requests without a key; only
        // send the Authorization header when a key is actually configured.
        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }
        if (!string.IsNullOrWhiteSpace(provider.OrganizationId))
        {
            request.Headers.Add("OpenAI-Organization", provider.OrganizationId);
        }
    }

    private static string BuildEndpoint(AiProvider provider)
    {
        var endpoint = (provider.EndpointUrl ?? string.Empty).Trim();
        var providerIsAzure = IsAzureProvider(provider);

        if (providerIsAzure)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException(
                    LocalizationManager.S("ai.runtime.error.missing_endpoint_azure",
                        "Para Azure OpenAI, configure o endpoint base do recurso."));
            }

            var baseEndpoint = endpoint.TrimEnd('/');
            var apiVersion = string.IsNullOrWhiteSpace(provider.ApiVersion)
                ? "2024-10-21"
                : provider.ApiVersion.Trim();

            return $"{baseEndpoint}/openai/deployments/{Uri.EscapeDataString(provider.ModelName!)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "https://api.openai.com/v1/chat/completions";
        }

        if (endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        return $"{endpoint.TrimEnd('/')}/v1/chat/completions";
    }

    private static bool IsAzureProvider(AiProvider provider)
    {
        var name = provider.ProviderName ?? string.Empty;
        return name.Contains("azure", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractContent(string responseContent)
    {
        using var json = JsonDocument.Parse(responseContent);
        if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                LocalizationManager.S("ai.runtime.error.invalid_response",
                    "Resposta inválida do provedor de IA."));
        }

        var first = choices[0];
        if (!first.TryGetProperty("message", out var message))
        {
            throw new InvalidOperationException(
                LocalizationManager.S("ai.runtime.error.invalid_response",
                    "Resposta inválida do provedor de IA."));
        }

        if (!message.TryGetProperty("content", out var contentElement))
        {
            return string.Empty;
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            return CleanupResult(contentElement.GetString() ?? string.Empty);
        }

        if (contentElement.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var item in contentElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var textNode))
                {
                    builder.Append(textNode.GetString());
                }
            }

            return CleanupResult(builder.ToString());
        }

        return string.Empty;
    }

    private static string CleanupResult(string text)
    {
        var value = text.Trim();
        value = StripMarkdownFenceWrapper(value);
        value = StripMarkdownFenceWrapper(value);
        return value.Trim();
    }

    private static string BuildAnswerPrompt(string question, string sourceContent)
    {
        return $"""
                You answer questions STRICTLY from the wiki pages provided below.

                Rules:
                - Use ONLY the information contained in the pages. Do not use outside knowledge or make assumptions.
                - If the answer is not present in the pages, clearly state that you could not find it in the loaded pages.
                - Answer in the SAME language as the question.
                - Cite the page title(s) you relied on, e.g. "(source: <page title>)".
                - Be concise and precise. Format the answer in Markdown.

                # Question
                {question}

                # Wiki pages
                ---
                {sourceContent}
                ---

                Answer:
                """;
    }

    private static string BuildSummaryPrompt(string sourceContent)
    {
        return $"""
                Create an executive summary in Markdown with: objective, scope, architecture, key rules and risks. Limit to 12 bullets.
                Keep the same Markdown writing style as the source content (heading/list density, spacing, and formatting richness).

                ---
                {sourceContent}
                """;
    }

    private static string BuildSummaryFromSummariesPrompt(string summariesContent)
    {
        return $"""
                # Task
                Synthesize the provided documentation into a high-level Executive Summary.
                Your goal is to abstract details into concepts, identifying patterns, objectives, and structural foundations.

                # Response Structure (Strictly Markdown)
                - ## Objective: The "Why". The fundamental problem being solved and the desired end-state.
                - ## Scope: The "What". The boundaries of the work, quantified by functional groups rather than individual items.
                - ## Architecture: The "How". The structural pillars, technologies, and methodologies that sustain the solution.
                - ## Key Rules: The "Logic". Essential constraints, governing principles, or critical algorithms described.
                - ## Risks: The "Caveats". Technical debts, dependencies, or external factors that require attention.

                # Guiding Principles for Intelligence
                1. **Abstraction over Repetition**: Do not list individual items (tables, files, names). Group them into logical categories (e.g., "19 core entities" instead of listing each one).
                2. **Semantic Density**: Prefer professional terminology that conveys complex ideas in few words (e.g., use "Idempotency" instead of "the ability to run multiple times without changing the result").
                3. **Cross-Reference**: Identify relationships between different sections of the input to avoid redundancy.
                4. **Agnostic Tone**: Maintain a technical, objective, and executive tone regardless of the subject matter.

                # Constraints
                - Accuracy: Do not hallucinate; use only the provided content.
                - Synthesis: Merge repeated rules/risks into single stronger bullets.
                - Density: Use short, impactful bullet points.
                - Total limit: Exactly 15 bullet points across all sections.
                - Output ONLY valid Markdown.
                - No conversational fillers or introductory text.

                # Input Content
                ---
                {summariesContent}
                ---

                Executive Summary:
                """;
    }

    private static string BuildIndexPrompt(string sourceContent)
    {
        return $"""
                Create a high-quality hierarchical index in Markdown from the source headings only.

                Mandatory output contract:
                - Output language: same as source language.
                - Preserve document style consistency, but force a stable and repeatable structure.
                - Use Markdown headings for top-level index sections and nested lists for deeper levels.
                - Add one icon to each top-level index heading (H1-equivalent sections), using the pattern: "## 📌 <Section Name>".
                - For level-2 and level-3 entries, use nested bullet lists only ("- item"), one item per line.
                - Every heading must be on its own line.
                - Every list item must be on its own line (no inline concatenation with " - ").
                - Insert a blank line after each top-level heading before its bullet list.
                - Maximum depth: 3 levels.
                - Do NOT mix numbering and bullets in the same index.
                - Do NOT create links/anchors.
                - Do NOT include paths, notebook names, URLs, metadata, or system labels.
                - Do NOT invent sections. Use only sections that exist in source headings.
                - Return ONLY Markdown index content (no intro/conclusion/explanations).

                Formatting rules:
                - Keep spacing clean and readable.
                - No inline concatenated items; every entry must be on its own line.
                - If source has weak heading structure, infer hierarchy conservatively from explicit heading markers only (#, ##, ###).

                Required output skeleton example (follow this shape):
                ## 📌 Section A

                - Subsection A.1
                  - Subsection A.1.1
                  - Subsection A.1.2
                - Subsection A.2

                ## 📌 Section B

                - Subsection B.1
                  - Subsection B.1.1

                ---
                {sourceContent}
                """;
    }

    private static string BuildQuizPrompt(string sourceContent, int directQuestions, int multipleChoiceQuestions)
    {
        var directCount = Math.Max(0, directQuestions);
        var multipleChoiceCount = Math.Max(0, multipleChoiceQuestions);

        return $"""
                # Role
                You are a senior assessment designer for technical documentation.

                # Task
                Create a high-quality questionnaire in Markdown based strictly on the provided content.

                # Hard constraints
                - Generate exactly {directCount} direct questions.
                - Generate exactly {multipleChoiceCount} multiple-choice questions.
                - Never change these counts.
                - Output language must match the source content language.
                - Use only facts present in the source content.
                - No filler text, no explanations outside the required sections.

                # Quality rules (mandatory)
                - Avoid generic questions like repeated "What is...?" unless unavoidable.
                - Prioritize applied understanding: purpose, tradeoff, rule, dependency, sequence, constraint.
                - Ensure coverage across distinct sections of the source, not only one topic.
                - For multiple-choice questions, create 3 plausible distractors and 1 correct option.
                - Do not use trick questions, ambiguity, or options like "all of the above".
                - Vary the position of correct options across A/B/C/D; avoid concentration in a single letter.
                - Do not repeat the same correct option letter in more than 2 consecutive questions.

                # Output format (strict Markdown)
                ## Direct Questions
                1. ...

                ## Multiple Choice Questions
                1. ...
                   - A) ...
                   - B) ...
                   - C) ...
                   - D) ...
                2. ...
                   - A) ...
                   - B) ...
                   - C) ...
                   - D) ...

                ## Answer Key
                ### Direct Questions
                1. <short objective answer>

                ### Multiple Choice Questions
                1. <correct option letter> - <one-line rationale grounded in the source>

                # Formatting compliance
                - In "Multiple Choice Questions", each option line MUST start with exactly "- A)", "- B)", "- C)", or "- D)".
                - Do not output plain options without these prefixes.
                - Keep blank lines between questions for readability.

                # Zero-count rule
                - If any section count is zero, keep the section and write "None".

                ---
                {sourceContent}
                """;
    }

    private static string StripMarkdownFenceWrapper(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var wrapper = Regex.Match(
            text,
            @"^\s*```(?:markdown|md)?\s*\r?\n([\s\S]*?)\r?\n```\s*$",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        return wrapper.Success ? wrapper.Groups[1].Value.Trim() : text;
    }

    private static void ValidatePromptSize(string userPrompt, int maxOutputTokens)
    {
        var estimatedInputTokens = EstimateTokenCount(SystemPrompt) + EstimateTokenCount(userPrompt);
        var safeInputBudget = Math.Max(2048, AssumedModelContextTokens - Math.Max(maxOutputTokens, 256));
        if (estimatedInputTokens <= safeInputBudget)
        {
            return;
        }

        throw new InvalidOperationException(
            LocalizationManager.S(
                "ai.runtime.error.input_too_large",
                "AI input is too large for a single request. Use the per-page scope or reduce selected pages."));
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // Heuristic: ~4 chars per token for mixed Markdown/technical text.
        return (text.Length / 4) + 1;
    }

    private static string TrimForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string? TryMapKnownProviderError(int statusCode, string responseContent)
    {
        var message = ExtractProviderErrorMessage(responseContent);
        if (statusCode == 401 ||
            message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationManager.S("ai.runtime.error.invalid_api_key",
                "Falha de autenticação no provedor de IA. Verifique a API Key.");
        }

        if (statusCode == 403 ||
            message.Contains("forbidden", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationManager.S("ai.runtime.error.forbidden",
                "O provedor de IA negou acesso a este recurso/modelo. Verifique permissões da conta e do modelo.");
        }

        if (statusCode == 429 ||
            message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("too many requests", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationManager.S("ai.runtime.error.rate_limit",
                "Limite de requisições atingido no provedor de IA. Aguarde alguns instantes e tente novamente.");
        }

        if (statusCode == 402 ||
            message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("insufficient quota", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationManager.S("ai.runtime.error.insufficient_balance",
                "O provedor de IA retornou saldo insuficiente. Recarregue os créditos e tente novamente.");
        }

        return null;
    }

    private static string ExtractProviderErrorMessage(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return string.Empty;
        }

        try
        {
            using var json = JsonDocument.Parse(responseContent);
            if (json.RootElement.TryGetProperty("error", out var errorNode))
            {
                if (errorNode.ValueKind == JsonValueKind.String)
                {
                    return errorNode.GetString() ?? string.Empty;
                }

                if (errorNode.ValueKind == JsonValueKind.Object &&
                    errorNode.TryGetProperty("message", out var messageNode))
                {
                    return messageNode.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            // Ignore parse issues and fallback to generic response handling.
        }

        return responseContent;
    }

    private sealed class ProviderRuntimeOptions
    {
        public double Temperature { get; init; } = 0.2;
        public int MaxTokens { get; init; } = 2000;
        public double? TopP { get; init; }
        public int TimeoutSeconds { get; init; } = 120;

        public static ProviderRuntimeOptions Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ProviderRuntimeOptions();
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var temperature = root.TryGetProperty("temperature", out var temperatureNode) && temperatureNode.TryGetDouble(out var t)
                    ? Math.Clamp(t, 0.0, 2.0)
                    : 0.2;
                var maxTokens = root.TryGetProperty("max_tokens", out var maxTokensNode) && maxTokensNode.TryGetInt32(out var m)
                    ? Math.Clamp(m, 128, 8192)
                    : 2000;
                double? topP = null;
                if (root.TryGetProperty("top_p", out var topPNode) && topPNode.TryGetDouble(out var p))
                {
                    topP = Math.Clamp(p, 0.0, 1.0);
                }
                var timeout = root.TryGetProperty("timeout_seconds", out var timeoutNode) && timeoutNode.TryGetInt32(out var s)
                    ? Math.Clamp(s, 10, 600)
                    : 120;

                return new ProviderRuntimeOptions
                {
                    Temperature = temperature,
                    MaxTokens = maxTokens,
                    TopP = topP,
                    TimeoutSeconds = timeout
                };
            }
            catch
            {
                return new ProviderRuntimeOptions();
            }
        }
    }
}
