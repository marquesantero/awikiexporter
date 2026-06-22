using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services.Authentication.Providers;

namespace ExportAzureWiki.Services;

/// <summary>
/// Factory service for creating OAuth provider instances from database configuration
/// </summary>
public class OAuthProviderFactoryService
{
    /// <summary>
    /// Creates an authentication provider from OAuth provider configuration
    /// </summary>
    /// <param name="config">OAuth provider configuration from database</param>
    /// <returns>Configured authentication provider</returns>
    /// <exception cref="NotSupportedException">Thrown when provider type is not supported</exception>
    public IAuthenticationProvider CreateProvider(OAuthProvider config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        if (!config.IsEnabled)
            throw new InvalidOperationException($"Provider {config.ProviderName} is disabled");

        var providerConfig = BuildProviderConfig(config);

        return config.ProviderName switch
        {
            "AzureAD" => new AzureADProvider(providerConfig),
            "GitHub" => new GitHubProvider(providerConfig),
            "Google" => new GoogleProvider(providerConfig),
            "Microsoft" => new MicrosoftAccountProvider(providerConfig),
            _ => throw new NotSupportedException($"Provider {config.ProviderName} is not supported")
        };
    }

    /// <summary>
    /// Creates multiple authentication providers from a list of configurations
    /// </summary>
    /// <param name="configs">List of OAuth provider configurations</param>
    /// <returns>List of configured authentication providers</returns>
    public IEnumerable<IAuthenticationProvider> CreateProviders(IEnumerable<OAuthProvider> configs)
    {
        var providers = new List<IAuthenticationProvider>();

        foreach (var config in configs)
        {
            try
            {
                if (config.IsEnabled)
                {
                    var provider = CreateProvider(config);
                    providers.Add(provider);
                }
            }
            catch (Exception ex)
            {
                // Log error but continue with other providers
                System.Diagnostics.Debug.WriteLine($"Error creating provider {config.ProviderName}: {ex.Message}");
            }
        }

        return providers;
    }

    private Dictionary<string, string> BuildProviderConfig(OAuthProvider provider)
    {
        // Secrets are handled at the data layer: OAuthProviderRepository
        // encrypts ClientSecret on write (StoredSecret.Protect) and decrypts it
        // on read (RevealSecrets), so by the time a provider reaches this factory
        // its ClientSecret is already plaintext. ClientId/TenantId are non-secret
        // identifiers stored as-is. Do NOT decrypt here -- that would
        // double-process an already-revealed value and corrupt it.
        var config = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(provider.ClientId))
        {
            config["ClientId"] = provider.ClientId;
        }

        if (!string.IsNullOrEmpty(provider.ClientSecret))
        {
            config["ClientSecret"] = provider.ClientSecret;
        }

        if (!string.IsNullOrEmpty(provider.TenantId))
        {
            config["TenantId"] = provider.TenantId;
        }

        if (!string.IsNullOrEmpty(provider.RedirectUri))
        {
            config["RedirectUri"] = provider.RedirectUri;
        }

        if (!string.IsNullOrEmpty(provider.Scopes))
        {
            config["Scopes"] = provider.Scopes;
        }

        // Parse additional configuration JSON if present
        if (!string.IsNullOrEmpty(provider.ConfigurationJson))
        {
            try
            {
                var additionalConfig = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(provider.ConfigurationJson);
                if (additionalConfig != null)
                {
                    foreach (var kvp in additionalConfig)
                    {
                        config[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch
            {
                // Invalid JSON, skip additional configuration
            }
        }

        return config;
    }
}
