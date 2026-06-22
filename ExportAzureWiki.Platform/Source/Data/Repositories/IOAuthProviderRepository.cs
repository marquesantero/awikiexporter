using ExportAzureWiki.Models;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository interface for OAuthProvider entities
/// </summary>
public interface IOAuthProviderRepository : IRepository<OAuthProvider>
{
    /// <summary>
    /// Gets an OAuth provider by name
    /// </summary>
    /// <param name="providerName">Provider name (e.g., AzureAD, GitHub)</param>
    /// <returns>OAuth provider if found, null otherwise</returns>
    Task<OAuthProvider?> GetByProviderNameAsync(string providerName);

    /// <summary>
    /// Gets all enabled OAuth providers
    /// </summary>
    /// <returns>List of enabled providers</returns>
    Task<IEnumerable<OAuthProvider>> GetEnabledProvidersAsync();

    /// <summary>
    /// Updates provider configuration
    /// </summary>
    /// <param name="id">Provider ID</param>
    /// <param name="clientId">Client ID (will be encrypted)</param>
    /// <param name="clientSecret">Client secret (will be encrypted)</param>
    /// <param name="additionalConfig">Additional configuration as JSON</param>
    /// <returns>True if successful</returns>
    Task<bool> UpdateProviderConfigAsync(int id, string clientId, string? clientSecret, string? additionalConfig);

    /// <summary>
    /// Enables or disables a provider
    /// </summary>
    /// <param name="id">Provider ID</param>
    /// <param name="isEnabled">Enable/disable flag</param>
    /// <returns>True if successful</returns>
    Task<bool> SetEnabledAsync(int id, bool isEnabled);

    /// <summary>
    /// Updates provider fields with explicit SQL to avoid accidental data loss from partial payloads.
    /// Secrets are preserved when omitted.
    /// </summary>
    /// <param name="provider">Provider payload</param>
    /// <returns>True if successful</returns>
    Task<bool> UpdateSafeAsync(OAuthProvider provider);
}
