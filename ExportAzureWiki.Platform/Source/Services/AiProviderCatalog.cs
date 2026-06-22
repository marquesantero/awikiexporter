namespace ExportAzureWiki.Services;

/// <summary>
/// A suggested AI provider preset. Presets are convenience pre-fills only --
/// they are NOT a closed list: the user can always pick "Custom" and type any
/// OpenAI-compatible base URL, and models are discovered live from the chosen
/// endpoint rather than hardcoded (both move too fast to freeze).
/// </summary>
/// <param name="Key">Stable identifier stored as the provider name.</param>
/// <param name="DisplayName">Human-friendly label shown in the dropdown.</param>
/// <param name="ChatEndpoint">
/// Full OpenAI-compatible chat-completions URL to pre-fill the endpoint field.
/// Empty means "use the OpenAI default" (or, for Azure, the user supplies the
/// resource endpoint).
/// </param>
/// <param name="IsLocal">True for locally hosted servers (no API key needed).</param>
/// <param name="IsAzure">True for Azure OpenAI (api-key header + deployments URL).</param>
public sealed record AiProviderPreset(
    string Key,
    string DisplayName,
    string ChatEndpoint,
    bool IsLocal = false,
    bool IsAzure = false);

/// <summary>
/// Curated-but-editable list of popular OpenAI-compatible providers (plus local
/// runtimes). Order is roughly by popularity. This list is a starting point,
/// not a constraint -- "OpenAICompatible" accepts any endpoint.
/// </summary>
public static class AiProviderCatalog
{
    public static IReadOnlyList<AiProviderPreset> Presets { get; } =
    [
        new("OpenAI", "OpenAI", "https://api.openai.com/v1/chat/completions"),
        new("AzureOpenAI", "Azure OpenAI", "", IsAzure: true),
        new("Anthropic", "Anthropic (Claude)", "https://api.anthropic.com/v1/chat/completions"),
        new("Gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"),
        new("Mistral", "Mistral AI", "https://api.mistral.ai/v1/chat/completions"),
        new("Groq", "Groq", "https://api.groq.com/openai/v1/chat/completions"),
        new("OpenRouter", "OpenRouter", "https://openrouter.ai/api/v1/chat/completions"),
        new("DeepSeek", "DeepSeek", "https://api.deepseek.com/v1/chat/completions"),
        new("Together", "Together AI", "https://api.together.xyz/v1/chat/completions"),
        new("Fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1/chat/completions"),
        new("xAI", "xAI (Grok)", "https://api.x.ai/v1/chat/completions"),
        new("Perplexity", "Perplexity", "https://api.perplexity.ai/chat/completions"),
        new("Cohere", "Cohere", "https://api.cohere.ai/compatibility/v1/chat/completions"),
        new("Ollama", "Ollama (local)", "http://localhost:11434/v1/chat/completions", IsLocal: true),
        new("LMStudio", "LM Studio (local)", "http://localhost:1234/v1/chat/completions", IsLocal: true),
        new("OpenAICompatible", "Custom (OpenAI-compatible)", ""),
    ];

    public static AiProviderPreset? Find(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : Presets.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
