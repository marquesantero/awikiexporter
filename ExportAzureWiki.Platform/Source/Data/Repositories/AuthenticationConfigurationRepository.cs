using System.Data;
using Dapper;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository for authentication configuration
/// </summary>
public class AuthenticationConfigurationRepository
{
    private readonly IDbConnection _connection;
    private readonly DatabaseType _databaseType;

    public AuthenticationConfigurationRepository(IDbConnection connection, DatabaseType databaseType)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _databaseType = databaseType;
    }

    /// <summary>
    /// Gets the current authentication configuration
    /// </summary>
    public async Task<AuthenticationConfiguration?> GetConfigurationAsync()
    {
        var sql = _databaseType == DatabaseType.SqlServer
            ? "SELECT TOP 1 * FROM [dbo].[AuthenticationConfiguration] ORDER BY [Id] DESC"
            : "SELECT * FROM authentication_configuration ORDER BY id DESC LIMIT 1";

        return await _connection.QueryFirstOrDefaultAsync<AuthenticationConfiguration>(sql).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves authentication configuration
    /// </summary>
    public async Task<bool> SaveConfigurationAsync(AuthenticationConfiguration config)
    {
        try
        {
            // Check if configuration exists
            var existing = await GetConfigurationAsync().ConfigureAwait(false);

            if (existing != null)
            {
                // Update existing
                return await UpdateConfigurationAsync(config).ConfigureAwait(false);
            }
            else
            {
                // Insert new
                return await InsertConfigurationAsync(config).ConfigureAwait(false);
            }
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> InsertConfigurationAsync(AuthenticationConfiguration config)
    {
        var sql = _databaseType == DatabaseType.SqlServer
            ? @"INSERT INTO [dbo].[AuthenticationConfiguration]
                ([PrimaryMethod], [AllowWindowsAuth], [AllowAzureAD], [AllowLocalAuth],
                 [RequireAuthentication], [SyncAzureADGroups], [SyncWindowsGroups],
                 [AzureADTenantId], [AutoCreateUsers], [DefaultRole],
                 [UseLocalPermissions], [UseAzureADPermissions], [UseWindowsPermissions])
                VALUES
                (@PrimaryMethod, @AllowWindowsAuth, @AllowAzureAD, @AllowLocalAuth,
                 @RequireAuthentication, @SyncAzureADGroups, @SyncWindowsGroups,
                 @AzureADTenantId, @AutoCreateUsers, @DefaultRole,
                 @UseLocalPermissions, @UseAzureADPermissions, @UseWindowsPermissions)"
            : @"INSERT INTO authentication_configuration
                (primary_method, allow_windows_auth, allow_azure_ad, allow_local_auth,
                 require_authentication, sync_azure_ad_groups, sync_windows_groups,
                 azure_ad_tenant_id, auto_create_users, default_role,
                 use_local_permissions, use_azure_ad_permissions, use_windows_permissions)
                VALUES
                (@PrimaryMethod, @AllowWindowsAuth, @AllowAzureAD, @AllowLocalAuth,
                 @RequireAuthentication, @SyncAzureADGroups, @SyncWindowsGroups,
                 @AzureADTenantId, @AutoCreateUsers, @DefaultRole,
                 @UseLocalPermissions, @UseAzureADPermissions, @UseWindowsPermissions)";

        var rows = await _connection.ExecuteAsync(sql, config).ConfigureAwait(false);
        return rows > 0;
    }

    private async Task<bool> UpdateConfigurationAsync(AuthenticationConfiguration config)
    {
        var sql = _databaseType == DatabaseType.SqlServer
            ? @"UPDATE [dbo].[AuthenticationConfiguration]
                SET [PrimaryMethod] = @PrimaryMethod,
                    [AllowWindowsAuth] = @AllowWindowsAuth,
                    [AllowAzureAD] = @AllowAzureAD,
                    [AllowLocalAuth] = @AllowLocalAuth,
                    [RequireAuthentication] = @RequireAuthentication,
                    [SyncAzureADGroups] = @SyncAzureADGroups,
                    [SyncWindowsGroups] = @SyncWindowsGroups,
                    [AzureADTenantId] = @AzureADTenantId,
                    [AutoCreateUsers] = @AutoCreateUsers,
                    [DefaultRole] = @DefaultRole,
                    [UseLocalPermissions] = @UseLocalPermissions,
                    [UseAzureADPermissions] = @UseAzureADPermissions,
                    [UseWindowsPermissions] = @UseWindowsPermissions,
                    [UpdatedAt] = GETDATE()
                WHERE [Id] = (SELECT TOP 1 [Id] FROM [dbo].[AuthenticationConfiguration] ORDER BY [Id] DESC)"
            : @"UPDATE authentication_configuration
                SET primary_method = @PrimaryMethod,
                    allow_windows_auth = @AllowWindowsAuth,
                    allow_azure_ad = @AllowAzureAD,
                    allow_local_auth = @AllowLocalAuth,
                    require_authentication = @RequireAuthentication,
                    sync_azure_ad_groups = @SyncAzureADGroups,
                    sync_windows_groups = @SyncWindowsGroups,
                    azure_ad_tenant_id = @AzureADTenantId,
                    auto_create_users = @AutoCreateUsers,
                    default_role = @DefaultRole,
                    use_local_permissions = @UseLocalPermissions,
                    use_azure_ad_permissions = @UseAzureADPermissions,
                    use_windows_permissions = @UseWindowsPermissions,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = (SELECT id FROM authentication_configuration ORDER BY id DESC LIMIT 1)";

        var rows = await _connection.ExecuteAsync(sql, config).ConfigureAwait(false);
        return rows > 0;
    }
}
