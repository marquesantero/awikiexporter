using ExportAzureWiki.Data;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Services.Authentication;

/// <summary>
/// Service for managing authentication configuration
/// </summary>
public class AuthenticationConfigService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private AuthenticationConfiguration? _cachedConfig;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    public AuthenticationConfigService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// Gets the current authentication configuration
    /// </summary>
    public async Task<AuthenticationConfiguration> GetConfigurationAsync(bool forceRefresh = false)
    {
        // Check cache
        if (!forceRefresh && _cachedConfig != null && DateTime.Now - _lastCacheUpdate < _cacheExpiration)
        {
            return _cachedConfig;
        }

        // Load from database. ConfigureAwait(false) is required because some
        // callers reach this via sync-over-async (GetConfiguration() and
        // IsConfigured() use .GetAwaiter().GetResult()); without it these awaits
        // would capture the WPF UI SynchronizationContext and deadlock against
        // the blocked caller.
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        var repository = new Data.Repositories.AuthenticationConfigurationRepository(
            connection,
            _connectionFactory.GetDatabaseType());

        var config = await repository.GetConfigurationAsync().ConfigureAwait(false);

        if (config == null)
        {
            // Return default configuration
            config = new AuthenticationConfiguration
            {
                PrimaryMethod = AuthenticationMethod.Local,
                AllowLocalAuth = true,
                RequireAuthentication = true,
                UseLocalPermissions = true,
                AutoCreateUsers = false
            };
        }

        // Update cache
        _cachedConfig = config;
        _lastCacheUpdate = DateTime.Now;

        return config;
    }

    /// <summary>
    /// Gets the current configuration synchronously
    /// </summary>
    public AuthenticationConfiguration GetConfiguration()
    {
        return GetConfigurationAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Checks if a specific authentication method is allowed
    /// </summary>
    public async Task<bool> IsMethodAllowedAsync(AuthenticationMethod method)
    {
        var config = await GetConfigurationAsync(forceRefresh: true).ConfigureAwait(false);

        return method switch
        {
            AuthenticationMethod.Local => config.AllowLocalAuth || config.PrimaryMethod == AuthenticationMethod.Local,
            AuthenticationMethod.Windows => config.AllowWindowsAuth || config.PrimaryMethod == AuthenticationMethod.Windows,
            AuthenticationMethod.AzureAD => config.AllowAzureAD || config.PrimaryMethod == AuthenticationMethod.AzureAD,
            AuthenticationMethod.OAuth => config.PrimaryMethod == AuthenticationMethod.OAuth,
            AuthenticationMethod.Multiple => config.PrimaryMethod == AuthenticationMethod.Multiple,
            _ => false
        };
    }

    /// <summary>
    /// Checks if local permissions are enabled
    /// </summary>
    public async Task<bool> UseLocalPermissionsAsync()
    {
        var config = await GetConfigurationAsync().ConfigureAwait(false);
        return config.UseLocalPermissions;
    }

    /// <summary>
    /// Checks if Azure AD permissions are enabled
    /// </summary>
    public async Task<bool> UseAzureADPermissionsAsync()
    {
        var config = await GetConfigurationAsync().ConfigureAwait(false);
        return config.UseAzureADPermissions;
    }

    /// <summary>
    /// Checks if Windows permissions are enabled
    /// </summary>
    public async Task<bool> UseWindowsPermissionsAsync()
    {
        var config = await GetConfigurationAsync().ConfigureAwait(false);
        return config.UseWindowsPermissions;
    }

    /// <summary>
    /// Clears the cache
    /// </summary>
    public void ClearCache()
    {
        _cachedConfig = null;
        _lastCacheUpdate = DateTime.MinValue;
    }
}
