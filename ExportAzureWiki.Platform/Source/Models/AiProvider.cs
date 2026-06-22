namespace ExportAzureWiki.Models;

/// <summary>
/// AI provider configuration stored in database.
/// Supports OpenAI-compatible and vendor-specific endpoints.
/// </summary>
public class AiProvider
{
    public int Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public string? EndpointUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ModelName { get; set; }
    public string? ApiVersion { get; set; }
    public string? OrganizationId { get; set; }
    public string? ConfigurationJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}
