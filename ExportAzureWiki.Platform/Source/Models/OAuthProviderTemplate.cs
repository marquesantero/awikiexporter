namespace ExportAzureWiki.Models;

/// <summary>
/// Template for OAuth provider configuration
/// </summary>
public class OAuthProviderTemplate
{
    /// <summary>
    /// Provider name (e.g., AzureAD, GitHub)
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// URL to register the application
    /// </summary>
    public string RegistrationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Help text for users
    /// </summary>
    public string HelpText { get; set; } = string.Empty;

    /// <summary>
    /// Default OAuth scopes
    /// </summary>
    public string DefaultScopes { get; set; } = string.Empty;

    /// <summary>
    /// Required configuration fields
    /// </summary>
    public List<ProviderField> RequiredFields { get; set; } = new();
}

/// <summary>
/// Field definition for OAuth provider configuration
/// </summary>
public class ProviderField
{
    /// <summary>
    /// Internal field name
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Label to display to user
    /// </summary>
    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>
    /// Whether the field contains sensitive data
    /// </summary>
    public bool IsSecret { get; set; }

    /// <summary>
    /// Whether the field is required
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Placeholder text for the field
    /// </summary>
    public string PlaceholderText { get; set; } = string.Empty;
}
