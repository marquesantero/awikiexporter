using System.Text.Json;
using Dapper;
using ExportAzureWiki.Data;

namespace ExportAzureWiki.Services;

public sealed class AdditionalCdnScriptService
{
    private const string SettingKey = "main.scripts.additional_cdns";
    private readonly IDbConnectionFactory _dbConnectionFactory = new DbConnectionFactory();

    public IReadOnlyList<string> Load()
    {
        try
        {
            using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
            var dbType = _dbConnectionFactory.GetDatabaseType();
            var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";
            var sql = dbType switch
            {
                DatabaseType.SqlServer => $"SELECT [Value] FROM {table} WHERE [Key] = @Key",
                DatabaseType.MySQL => $"SELECT value FROM {table} WHERE `key` = @Key",
                _ => $"SELECT value FROM {table} WHERE key = @Key"
            };

            var value = connection.QueryFirstOrDefault<string>(sql, new { Key = SettingKey });
            if (string.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            var parsed = JsonSerializer.Deserialize<List<string>>(value) ?? [];
            return Sanitize(parsed);
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<string> urls)
    {
        var sanitized = Sanitize(urls);
        var payload = JsonSerializer.Serialize(sanitized, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
        var dbType = _dbConnectionFactory.GetDatabaseType();
        var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";

        if (dbType == DatabaseType.SqlServer)
        {
            connection.Execute(
                $"""
                 MERGE {table} AS target
                 USING (SELECT @Key AS [Key]) AS source
                 ON target.[Key] = source.[Key]
                 WHEN MATCHED THEN
                     UPDATE SET [Value] = @Value, [IsEncrypted] = 0, [LastModifiedAt] = GETDATE()
                 WHEN NOT MATCHED THEN
                     INSERT ([Key], [Value], [IsEncrypted], [LastModifiedAt])
                     VALUES (@Key, @Value, 0, GETDATE());
                 """,
                new { Key = SettingKey, Value = payload });
            return;
        }

        if (dbType == DatabaseType.MySQL)
        {
            connection.Execute(
                $"""
                 INSERT INTO {table} (`key`, value, is_encrypted, last_modified_at)
                 VALUES (@Key, @Value, 0, CURRENT_TIMESTAMP)
                 ON DUPLICATE KEY UPDATE
                     value = VALUES(value),
                     is_encrypted = 0,
                     last_modified_at = CURRENT_TIMESTAMP
                 """,
                new { Key = SettingKey, Value = payload });
            return;
        }

        connection.Execute(
            $"""
             INSERT INTO {table} (key, value, is_encrypted, last_modified_at)
             VALUES (@Key, @Value, 0, CURRENT_TIMESTAMP)
             ON CONFLICT(key) DO UPDATE SET
                 value = excluded.value,
                 is_encrypted = 0,
                 last_modified_at = CURRENT_TIMESTAMP
             """,
            new { Key = SettingKey, Value = payload });
    }

    public static bool IsValidUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static IReadOnlyList<string> Sanitize(IEnumerable<string> urls)
    {
        return urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Where(IsValidUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
