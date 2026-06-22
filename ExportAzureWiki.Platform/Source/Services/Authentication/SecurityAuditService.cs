using System.Text.Json;
using Dapper;
using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Data;
using Serilog;

namespace ExportAzureWiki.Services.Authentication;

/// <summary>
/// Writes and reads the security audit log. Every entry is persisted to
/// the database (so the admin viewer can browse it across machines and
/// restarts) and also emitted to the Serilog file sink so an operator can
/// correlate the event with the surrounding application activity without
/// joining tables.
///
/// Writes are best-effort: a failure to persist is logged but never
/// propagates back to the caller. Audit logging must not block a login
/// or break an admin action.
/// </summary>
public sealed class SecurityAuditService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SecurityAuditService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// Convenience overload for the common login-flow shape.
    /// </summary>
    public Task RecordAsync(
        string eventType,
        int? userId,
        string? username,
        object? detail = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        return RecordAsync(new SecurityAuditEntry
        {
            EventType = eventType,
            OccurredAt = DateTime.UtcNow,
            UserId = userId,
            Username = username,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Detail = detail is null
                ? null
                : detail is string s ? s : JsonSerializer.Serialize(detail),
        });
    }

    public async Task RecordAsync(SecurityAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Surface the event to the Serilog file sink first. That sink
        // always reaches disk; the DB insert can race with shutdown or
        // hit a connection issue. Operators can still reconstruct events
        // from the log file if the DB write fails below.
        Log.Information(
            "audit {EventType} user={UserId} username={Username}",
            entry.EventType, entry.UserId, entry.Username);

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
            var dbType = _connectionFactory.GetDatabaseType();
            var sql = dbType switch
            {
                DatabaseType.SqlServer => """
                    INSERT INTO [dbo].[SecurityAuditLog]
                        ([OccurredAt], [EventType], [UserId], [Username], [IpAddress], [UserAgent], [Detail])
                    VALUES
                        (@OccurredAt, @EventType, @UserId, @Username, @IpAddress, @UserAgent, @Detail)
                    """,
                DatabaseType.PostgreSQL => """
                    INSERT INTO security_audit_log
                        (occurred_at, event_type, user_id, username, ip_address, user_agent, detail)
                    VALUES
                        (@OccurredAt, @EventType, @UserId, @Username, @IpAddress, @UserAgent, @Detail)
                    """,
                DatabaseType.MySQL => """
                    INSERT INTO security_audit_log
                        (occurred_at, event_type, user_id, username, ip_address, user_agent, detail)
                    VALUES
                        (@OccurredAt, @EventType, @UserId, @Username, @IpAddress, @UserAgent, @Detail)
                    """,
                _ => """
                    INSERT INTO security_audit_log
                        (occurred_at, event_type, user_id, username, ip_address, user_agent, detail)
                    VALUES
                        (@OccurredAt, @EventType, @UserId, @Username, @IpAddress, @UserAgent, @Detail)
                    """,
            };

            await connection.ExecuteAsync(sql, new
            {
                entry.OccurredAt,
                entry.EventType,
                entry.UserId,
                entry.Username,
                entry.IpAddress,
                entry.UserAgent,
                entry.Detail,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fail-open intentionally: the caller's flow must not break
            // because the audit table is unreachable. The log line above
            // is the durable copy.
            Log.Error(ex,
                "Failed to persist security audit entry {EventType} for user {UserId}",
                entry.EventType, entry.UserId);
        }
    }

    /// <summary>
    /// Reads the most recent entries (descending by OccurredAt) for the
    /// admin viewer. Cap is honoured by the caller.
    /// </summary>
    public async Task<IReadOnlyList<SecurityAuditEntry>> ListRecentAsync(int max = 200)
    {
        if (max <= 0)
        {
            return Array.Empty<SecurityAuditEntry>();
        }

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
            var dbType = _connectionFactory.GetDatabaseType();
            var sql = dbType switch
            {
                DatabaseType.SqlServer => $"""
                    SELECT TOP ({max})
                        [Id]         AS Id,
                        [OccurredAt] AS OccurredAt,
                        [EventType]  AS EventType,
                        [UserId]     AS UserId,
                        [Username]   AS Username,
                        [IpAddress]  AS IpAddress,
                        [UserAgent]  AS UserAgent,
                        [Detail]     AS Detail
                    FROM [dbo].[SecurityAuditLog]
                    ORDER BY [OccurredAt] DESC
                    """,
                _ => $"""
                    SELECT
                        id          AS Id,
                        occurred_at AS OccurredAt,
                        event_type  AS EventType,
                        user_id     AS UserId,
                        username    AS Username,
                        ip_address  AS IpAddress,
                        user_agent  AS UserAgent,
                        detail      AS Detail
                    FROM security_audit_log
                    ORDER BY occurred_at DESC
                    LIMIT {max}
                    """,
            };

            var rows = await connection.QueryAsync<SecurityAuditEntry>(sql).ConfigureAwait(false);
            return rows.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read security audit log");
            return Array.Empty<SecurityAuditEntry>();
        }
    }
}
