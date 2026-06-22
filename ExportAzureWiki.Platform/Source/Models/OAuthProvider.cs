namespace ExportAzureWiki.Models;

/// <summary>
/// OAuth provider configuration stored in database
/// </summary>
public class OAuthProvider
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Provider name (AzureAD, GitHub, Google, Microsoft)
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown in UI
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the provider is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// OAuth Client ID (encrypted in database)
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth Client Secret (encrypted in database, nullable for implicit flow)
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Tenant ID for Azure AD (encrypted in database)
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Redirect URI for OAuth callback
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// OAuth scopes (space-separated)
    /// </summary>
    public string? Scopes { get; set; }

    /// <summary>
    /// Additional configuration as JSON
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// When the provider was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the provider was last modified
    /// </summary>
    public DateTime? LastModifiedAt { get; set; }
}
