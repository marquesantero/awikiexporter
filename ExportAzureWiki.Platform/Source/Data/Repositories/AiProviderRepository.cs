using System.Data;
using Dapper;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Data.Repositories;

public class AiProviderRepository : BaseRepository<AiProvider>, IAiProviderRepository
{
    public AiProviderRepository(IDbConnection connection, DatabaseType databaseType)
        : base(connection, databaseType, databaseType == DatabaseType.SqlServer ? "AiProviders" : "ai_providers")
    {
    }

    // Encrypt the API key before it reaches the (reflection-based) base
    // INSERT/UPDATE, mirroring OAuthProviderRepository. Protect/Reveal are
    // idempotent: legacy plaintext rows are read as-is and rewritten as
    // enc:... on the next save. See Fase 1.6.
    public override async Task<int> AddAsync(AiProvider entity)
    {
        if (!string.IsNullOrEmpty(entity?.ApiKey))
        {
            entity.ApiKey = StoredSecret.Protect(entity.ApiKey);
        }
        return await base.AddAsync(entity!).ConfigureAwait(false);
    }

    public override async Task<bool> UpdateAsync(AiProvider entity)
    {
        if (!string.IsNullOrEmpty(entity?.ApiKey))
        {
            entity.ApiKey = StoredSecret.Protect(entity.ApiKey);
        }
        return await base.UpdateAsync(entity!).ConfigureAwait(false);
    }

    public override async Task<AiProvider?> GetByIdAsync(int id)
        => RevealSecrets(await base.GetByIdAsync(id).ConfigureAwait(false));

    public override async Task<IEnumerable<AiProvider>> GetAllAsync()
        => (await base.GetAllAsync().ConfigureAwait(false)).Select(RevealSecrets!).ToList()!;

    public async Task<AiProvider?> GetByProviderNameAsync(string providerName)
    {
        var nameCol = DatabaseType == DatabaseType.SqlServer ? "ProviderName" : "provider_name";
        var sql = DatabaseType == DatabaseType.SqlServer
            ? $"SELECT * FROM {GetQualifiedTableName()} WHERE {nameCol} = @ProviderName"
            : $"SELECT * FROM {GetQualifiedTableName()} WHERE LOWER({nameCol}) = LOWER(@ProviderName)";
        var provider = await Connection.QuerySingleOrDefaultAsync<AiProvider>(sql, new { ProviderName = providerName }).ConfigureAwait(false);
        return RevealSecrets(provider);
    }

    public async Task<IEnumerable<AiProvider>> GetEnabledProvidersAsync()
    {
        var enabledCol = DatabaseType == DatabaseType.SqlServer ? "IsEnabled" : "is_enabled";
        var sql = $"SELECT * FROM {GetQualifiedTableName()} WHERE {enabledCol} = @IsEnabled";
        var providers = await Connection.QueryAsync<AiProvider>(sql, new { IsEnabled = true }).ConfigureAwait(false);
        return providers.Select(RevealSecrets!).ToList()!;
    }

    public async Task<AiProvider?> GetDefaultProviderAsync()
    {
        var defaultCol = DatabaseType == DatabaseType.SqlServer ? "IsDefault" : "is_default";
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var limit = DatabaseType == DatabaseType.SqlServer ? "TOP 1" : string.Empty;
        var tail = DatabaseType == DatabaseType.SqlServer ? string.Empty : "LIMIT 1";
        var sql = $"SELECT {limit} * FROM {GetQualifiedTableName()} WHERE {defaultCol} = 1 ORDER BY {idCol} {tail}";
        var provider = await Connection.QueryFirstOrDefaultAsync<AiProvider>(sql).ConfigureAwait(false);
        return RevealSecrets(provider);
    }

    /// <summary>
    /// Decrypts the ApiKey stored under the enc: prefix. Legacy plaintext
    /// rows pass through unchanged.
    /// </summary>
    private static AiProvider? RevealSecrets(AiProvider? provider)
    {
        if (provider is null)
        {
            return null;
        }
        provider.ApiKey = StoredSecret.Reveal(provider.ApiKey);
        return provider;
    }

    public async Task<bool> SetEnabledAsync(int id, bool isEnabled)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var enabledCol = DatabaseType == DatabaseType.SqlServer ? "IsEnabled" : "is_enabled";
        var sql = $"UPDATE {GetQualifiedTableName()} SET {enabledCol} = @IsEnabled WHERE {idCol} = @Id";
        var rows = await Connection.ExecuteAsync(sql, new { Id = id, IsEnabled = isEnabled }).ConfigureAwait(false);
        return rows > 0;
    }

    public async Task<bool> SetAsDefaultAsync(int id)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var defaultCol = DatabaseType == DatabaseType.SqlServer ? "IsDefault" : "is_default";

        using var tx = Connection.BeginTransaction();
        try
        {
            var resetSql = $"UPDATE {GetQualifiedTableName()} SET {defaultCol} = 0";
            await Connection.ExecuteAsync(resetSql, transaction: tx).ConfigureAwait(false);

            var setSql = $"UPDATE {GetQualifiedTableName()} SET {defaultCol} = 1 WHERE {idCol} = @Id";
            var rows = await Connection.ExecuteAsync(setSql, new { Id = id }, tx).ConfigureAwait(false);
            tx.Commit();
            return rows > 0;
        }
        catch
        {
            tx.Rollback();
            return false;
        }
    }

    public async Task<bool> UpdateSafeAsync(AiProvider provider)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var providerNameCol = DatabaseType == DatabaseType.SqlServer ? "ProviderName" : "provider_name";
        var displayNameCol = DatabaseType == DatabaseType.SqlServer ? "DisplayName" : "display_name";
        var isEnabledCol = DatabaseType == DatabaseType.SqlServer ? "IsEnabled" : "is_enabled";
        var isDefaultCol = DatabaseType == DatabaseType.SqlServer ? "IsDefault" : "is_default";
        var endpointCol = DatabaseType == DatabaseType.SqlServer ? "EndpointUrl" : "endpoint_url";
        var apiKeyCol = DatabaseType == DatabaseType.SqlServer ? "ApiKey" : "api_key";
        var modelCol = DatabaseType == DatabaseType.SqlServer ? "ModelName" : "model_name";
        var apiVersionCol = DatabaseType == DatabaseType.SqlServer ? "ApiVersion" : "api_version";
        var organizationCol = DatabaseType == DatabaseType.SqlServer ? "OrganizationId" : "organization_id";
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
    {isDefaultCol} = @IsDefault,
    {endpointCol} = @EndpointUrl,
    {apiKeyCol} = COALESCE(@ApiKey, {apiKeyCol}),
    {modelCol} = @ModelName,
    {apiVersionCol} = @ApiVersion,
    {organizationCol} = @OrganizationId,
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
            provider.IsDefault,
            provider.EndpointUrl,
            // COALESCE keeps the existing key when null is passed (the admin
            // UI sends null to mean "unchanged"); otherwise encrypt it.
            ApiKey = string.IsNullOrEmpty(provider.ApiKey)
                ? null
                : StoredSecret.Protect(provider.ApiKey),
            provider.ModelName,
            provider.ApiVersion,
            provider.OrganizationId,
            provider.ConfigurationJson,
            CreatedAt = provider.CreatedAt == default ? (DateTime?)null : provider.CreatedAt
        }).ConfigureAwait(false);

        return rowsAffected > 0;
    }
}
