using ExportAzureWiki.Models;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Covers the pure helpers behind dynamic model discovery and the provider
/// preset catalog (no network).
/// </summary>
public sealed class AiProviderProbeTests
{
    [Fact]
    public void ParseModelIds_OpenAiShape_ReturnsSortedDistinctIds()
    {
        const string json = """
        { "object": "list", "data": [ { "id": "gpt-4o" }, { "id": "gpt-4o-mini" }, { "id": "gpt-4o" } ] }
        """;

        AiTextOperationsService.ParseModelIds(json)
            .Should().Equal("gpt-4o", "gpt-4o-mini");
    }

    [Fact]
    public void ParseModelIds_BareArray_AndNameFallback()
    {
        // Some local servers return a top-level array and use "name".
        const string json = """
        [ { "name": "llama3.1" }, { "name": "qwen2.5" } ]
        """;

        AiTextOperationsService.ParseModelIds(json)
            .Should().Equal("llama3.1", "qwen2.5");
    }

    [Fact]
    public void ParseModelIds_EmptyOrInvalid_ReturnsEmpty()
    {
        AiTextOperationsService.ParseModelIds("{}").Should().BeEmpty();
        AiTextOperationsService.ParseModelIds("""{ "data": [] }""").Should().BeEmpty();
        AiTextOperationsService.ParseModelIds("").Should().BeEmpty();
    }

    [Fact]
    public void BuildModelsEndpoint_DefaultsToOpenAi_WhenEndpointBlank()
    {
        var provider = new AiProvider { ProviderName = "OpenAI", EndpointUrl = "" };
        AiTextOperationsService.BuildModelsEndpoint(provider)
            .Should().Be("https://api.openai.com/v1/models");
    }

    [Fact]
    public void BuildModelsEndpoint_DerivesFromChatCompletionsEndpoint()
    {
        var provider = new AiProvider
        {
            ProviderName = "Groq",
            EndpointUrl = "https://api.groq.com/openai/v1/chat/completions"
        };

        AiTextOperationsService.BuildModelsEndpoint(provider)
            .Should().Be("https://api.groq.com/openai/v1/models");
    }

    [Fact]
    public void BuildModelsEndpoint_AppendsV1Models_ForBareBase()
    {
        var provider = new AiProvider
        {
            ProviderName = "Ollama",
            EndpointUrl = "http://localhost:11434/"
        };

        AiTextOperationsService.BuildModelsEndpoint(provider)
            .Should().Be("http://localhost:11434/v1/models");
    }

    [Fact]
    public void BuildModelsEndpoint_Azure_UsesDeploymentsWithApiVersion()
    {
        var provider = new AiProvider
        {
            ProviderName = "AzureOpenAI",
            EndpointUrl = "https://my-res.openai.azure.com",
            ApiVersion = "2024-10-21"
        };

        AiTextOperationsService.BuildModelsEndpoint(provider)
            .Should().Be("https://my-res.openai.azure.com/openai/deployments?api-version=2024-10-21");
    }

    [Fact]
    public void Catalog_HasPopularPresets_AndCustomAndLocal()
    {
        var keys = AiProviderCatalog.Presets.Select(p => p.Key).ToList();

        keys.Should().Contain(["OpenAI", "AzureOpenAI", "Anthropic", "Gemini", "OpenAICompatible"]);
        AiProviderCatalog.Presets.Should().Contain(p => p.IsLocal);     // Ollama / LM Studio
        AiProviderCatalog.Presets.Should().Contain(p => p.IsAzure);     // Azure OpenAI
        AiProviderCatalog.Presets.Count.Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void Catalog_Find_IsCaseInsensitive_AndNullForUnknown()
    {
        AiProviderCatalog.Find("openai")!.Key.Should().Be("OpenAI");
        AiProviderCatalog.Find("does-not-exist").Should().BeNull();
    }
}
