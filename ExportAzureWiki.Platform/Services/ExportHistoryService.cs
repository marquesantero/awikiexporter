using System.Text.Json;
using Dapper;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Data;

namespace ExportAzureWiki.Platform.Services;

public sealed class ExportHistoryService : IExportHistoryService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ExportHistoryService()
        : this(new DbConnectionFactory())
    {
    }

    internal ExportHistoryService(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task RecordAsync(ExportHistoryEntry entry)
    {
        try
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync().ConfigureAwait(false);
            var databaseType = _dbConnectionFactory.GetDatabaseType();
            var userId = ParseNullableInt(entry.UserId);
            var action = entry.Success
                ? $"EXPORT_{entry.Format.ToUpperInvariant()}_SUCCESS"
                : $"EXPORT_{entry.Format.ToUpperInvariant()}_FAILED";

            var detailsPayload = new
            {
                format = entry.Format,
                scope = entry.Scope,
                outputPath = entry.OutputPath,
                sourcePages = entry.SourcePages,
                success = entry.Success,
                durationMs = entry.DurationMs,
                errorMessage = entry.ErrorMessage,
                user = entry.Username,
                custom = entry.DetailsJson
            };

            var details = JsonSerializer.Serialize(detailsPayload);

            var sql = databaseType == DatabaseType.SqlServer
                ? """
                  INSERT INTO [dbo].[AuditLog] ([UserId], [Action], [EntityType], [EntityId], [Details], [IpAddress], [Timestamp])
                  VALUES (@UserId, @Action, @EntityType, @EntityId, @Details, @IpAddress, @Timestamp)
                  """
                : """
                  INSERT INTO audit_log (user_id, action, entity_type, entity_id, details, ip_address, timestamp)
                  VALUES (@UserId, @Action, @EntityType, @EntityId, @Details, @IpAddress, @Timestamp)
                  """;

            await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                Action = action,
                EntityType = "Export",
                EntityId = (int?)null,
                Details = details,
                IpAddress = (string?)null,
                Timestamp = entry.Timestamp == default ? DateTime.UtcNow : entry.Timestamp
            }).ConfigureAwait(false);
        }
        catch
        {
            // Export history must never break export flow.
        }
    }

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}

