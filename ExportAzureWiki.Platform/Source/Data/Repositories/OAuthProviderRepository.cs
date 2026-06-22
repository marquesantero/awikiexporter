using System.Data;
using Dapper;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository implementation for OAuthProvider entities
/// </summary>
public class OAuthProviderRepository : BaseRepository<OAuthProvider>, IOAuthProviderRepository
{
    public OAuthProviderRepository(IDbConnection connection, DatabaseType databaseType)
        : base(connection, databaseType, databaseType == DatabaseType.SqlServer ? "OAuthProviders" : "oauth_providers")
    {
    }

    public override async Task<int> AddAsync(OAuthProvider entity)
    {
        // Defense in depth: BaseRepository.AddAsync uses reflection to build
        // the INSERT, so the ClientSecret property is passed straight through
        // to Dapper. Encrypt it on the entity before that path runs so the
        // value at rest is never plaintext.
        if (!string.IsNullOrEmpty(entity.ClientSecret))
        {
            entity.ClientSecret = StoredSecret.Protect(entity.ClientSecret);
        }
        return await base.AddAsync(entity).ConfigureAwait(false);
    }

    public override async Task<bool> UpdateAsync(OAuthProvider entity)
    {
        if (!string.IsNullOrEmpty(entity.ClientSecret))
        {
            entity.ClientSecret = StoredSecret.Protect(entity.ClientSecret);
        }
        return await base.UpdateAsync(entity).ConfigureAwait(false);
    }

    public async Task<OAuthProvider?> GetByProviderNameAsync(string providerName)
    {
        var columnName = DatabaseType == DatabaseType.SqlServer ? "ProviderName" : "provider_name";
        var sql = DatabaseType == DatabaseType.SqlServer
            ? $"SELECT * FROM {GetQualifiedTableName()} WHERE {columnName} = @ProviderName"
            : $"SELECT * FROM {GetQualifiedTableName()} WHERE LOWER({columnName}) = LOWER(@ProviderName)";
        var provider = await Connection.QuerySingleOrDefaultAsync<OAuthProvider>(sql, new { ProviderName = providerName }).ConfigureAwait(false);
        return RevealSecrets(provider);
    }

    public async Task<IEnumerable<OAuthProvider>> GetEnabledProvidersAsync()
    {
        var columnName = DatabaseType == DatabaseType.SqlServer ? "IsEnabled" : "is_enabled";
        var sql = $"SELECT * FROM {GetQualifiedTableName()} WHERE {columnName} = @IsEnabled";
        var providers = await Connection.QueryAsync<OAuthProvider>(sql, new { IsEnabled = true }).ConfigureAwait(false);
        return providers.Select(RevealSecrets!).ToList()!;
    }

    /// <summary>
    /// Decrypts the ClientSecret stored under the enc: prefix. Legacy rows
    /// written before Fase 1.6 contain plaintext and pass through unchanged.
    /// </summary>
    private static OAuthProvider? RevealSecrets(OAuthProvider? provider)
    {
        if (provider is null)
        {
            return null;
        }

        provider.ClientSecret = StoredSecret.Reveal(provider.ClientSecret);
        return provider;
    }

    public async Task<bool> UpdateProviderConfigAsync(int id, string clientId, string? clientSecret, string? additionalConfig)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var clientIdCol = DatabaseType == DatabaseType.SqlServer ? "ClientId" : "client_id";
        var clientSecretCol = DatabaseType == DatabaseType.SqlServer ? "ClientSecret" : "client_secret";
        var configCol = DatabaseType == DatabaseType.SqlServer ? "ConfigurationJson" : "configuration_json";
        var modifiedCol = DatabaseType == DatabaseType.SqlServer ? "LastModifiedAt" : "last_modified_at";

        var now = DatabaseType == DatabaseType.SQLite
            ? "datetime('now')"
            : DatabaseType == DatabaseType.SqlServer
                ? "GETDATE()"
                : "CURRENT_TIMESTAMP";

        var sql = $@"UPDATE {GetQualifiedTableName()}
                     SET {clientIdCol} = @ClientId,
                         {clientSecretCol} = @ClientSecret,
                         {configCol} = @ConfigurationJson,
                         {modifiedCol} = {now}
                     WHERE {idCol} = @Id";

        var rowsAffected = await Connection.ExecuteAsync(sql, new
        {
            Id = id,
            ClientId = clientId,
            ClientSecret = StoredSecret.Protect(clientSecret),
            ConfigurationJson = additionalConfig
        }).ConfigureAwait(false);

        return rowsAffected > 0;
    }

    public async Task<bool> SetEnabledAsync(int id, bool isEnabled)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var enabledCol = DatabaseType == DatabaseType.SqlServer ? "IsEnabled" : "is_enabled";

        var sql = $"UPDATE {GetQualifiedTableName()} SET {enabledCol} = @IsEnabled WHERE {idCol} = @Id";
        var rowsAffected = await Connection.ExecuteAsync(sql, new { Id = id, IsEnabled = isEnabled }).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateSafeAsync(OAuthProvider provider)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var providerNameCol = DatabaseType == DatabaseType.SqlServer ? "ProviderName" : "provider_name";
        var displayNameCol = DatabaseType == DatabaseType.SqlServer ? "DisplayName" : "display_name";
        var isEnabledCol = DatabaseType == DatabaseType.SqlServer ? "IsEnabled" : "is_enabled";
        var clientIdCol = DatabaseType == DatabaseType.SqlServer ? "ClientId" : "client_id";
        var clientSecretCol = DatabaseType == DatabaseType.SqlServer ? "ClientSecret" : "client_secret";
        var tenantIdCol = DatabaseType == DatabaseType.SqlServer ? "TenantId" : "tenant_id";
        var redirectUriCol = DatabaseType == DatabaseType.SqlServer ? "RedirectUri" : "redirect_uri";
        var scopesCol = DatabaseType == DatabaseType.SqlServer ? "Scopes" : "scopes";
        var configCol = DatabaseType == DatabaseType.SqlServer ? "ConfigurationJson" : "configuration_json";
        var createdAtCol = DatabaseType == DatabaseType.SqlServer ? "CreatedAt" : "created_at";
        var lastModifiedAtCol = DatabaseType == DatabaseType.SqlServer ? "LastModifiedAt" : "last_modified_at";

        var now = DatabaseType == DatabaseType.SQLite
            ? "datetime('now')"
            : DatabaseType == DatabaseType.SqlServer
                ? "GETDATE()"
                : "CURRENT_TIMESTAMP";

        var sql = $@"
UPDATE {GetQualifiedTableName()}
SET {providerNameCol} = @ProviderName,
    {displayNameCol} = @DisplayName,
    {isEnabledCol} = @IsEnabled,
    {clientIdCol} = @ClientId,
    {clientSecretCol} = COALESCE(@ClientSecret, {clientSecretCol}),
    {tenantIdCol} = @TenantId,
    {redirectUriCol} = @RedirectUri,
    {scopesCol} = @Scopes,
    {configCol} = @ConfigurationJson,
    {createdAtCol} = COALESCE(@CreatedAt, {createdAtCol}),
    {lastModifiedAtCol} = {now}
WHERE {idCol} = @Id";

        var rowsAffected = await Connection.ExecuteAsync(sql, new
        {
            provider.Id,
            provider.ProviderName,
            provider.DisplayName,
            provider.IsEnabled,
            provider.ClientId,
            ClientSecret = string.IsNullOrEmpty(provider.ClientSecret)
                ? null
                : StoredSecret.Protect(provider.ClientSecret),
            provider.TenantId,
            provider.RedirectUri,
            provider.Scopes,
            provider.ConfigurationJson,
            CreatedAt = provider.CreatedAt == default ? (DateTime?)null : provider.CreatedAt
        }).ConfigureAwait(false);

        return rowsAffected > 0;
    }
}
