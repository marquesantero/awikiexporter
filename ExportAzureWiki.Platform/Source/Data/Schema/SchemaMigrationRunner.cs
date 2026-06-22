using System.Data;
using Dapper;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Data.Schema;

/// <summary>
/// A migration step: a stable id, a human description, and the work that
/// brings the schema to that state. The work must be idempotent -- the
/// runner skips a step whose id is already journaled, but a half-applied
/// step (process killed mid-migration before the journal row committed)
/// will run again on the next boot, so the work should tolerate being
/// re-run (the existing <c>Ensure*</c> helpers already do: they probe
/// <c>ColumnExists</c> / <c>TableExists</c> first).
/// </summary>
public sealed record SchemaMigration(string Id, string Description, Func<IDbConnection, Task> ApplyAsync);

/// <summary>
/// Ordered, journaled migration runner. Replaces the previous ad-hoc
/// "version 2 only" stamping with a durable record of every structural
/// change that has been applied, keyed by a stable id.
///
/// The journal lives in <c>schema_migrations</c> (one row per applied
/// migration id, with the UTC timestamp). An operator can read it to see
/// exactly which upgrades a given database has received and when -- the
/// auditable upgrade path the remediation plan calls for, without the
/// risk of swapping the working schema bootstrap for a wholesale DbUp
/// rewrite that cannot be integration-tested across all four engines
/// here.
///
/// The work and its journal row are NOT wrapped in a single transaction.
/// Threading a transaction through every Ensure* helper would mean passing
/// it to every Dapper call (SqlClient throws on a pending local transaction
/// that is not assigned to the command), and MySQL auto-commits DDL anyway.
/// Instead the contract relies on each step being idempotent: if the process
/// dies after the work commits but before the journal row is written, the
/// step simply runs again (harmlessly) on the next boot and is journaled
/// then. The existing Ensure* helpers all probe TableExists / ColumnExists
/// before acting, so re-running is a no-op.
/// </summary>
public sealed class SchemaMigrationRunner
{
    private readonly IDbConnection _connection;
    private readonly DatabaseType _databaseType;

    public SchemaMigrationRunner(IDbConnection connection, DatabaseType databaseType)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _databaseType = databaseType;
    }

    public async Task EnsureJournalTableAsync()
    {
        var sql = _databaseType switch
        {
            DatabaseType.SqlServer => """
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SchemaMigrations]') AND type = N'U')
                CREATE TABLE [dbo].[SchemaMigrations] (
                    [Id] NVARCHAR(128) NOT NULL PRIMARY KEY,
                    [Description] NVARCHAR(512) NULL,
                    [AppliedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
                """,
            DatabaseType.PostgreSQL => """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id          VARCHAR(128) NOT NULL PRIMARY KEY,
                    description VARCHAR(512) NULL,
                    applied_at  TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """,
            DatabaseType.MySQL => """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id          VARCHAR(128) NOT NULL PRIMARY KEY,
                    description VARCHAR(512) NULL,
                    applied_at  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
                """,
            DatabaseType.SQLite => """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id          TEXT NOT NULL PRIMARY KEY,
                    description TEXT NULL,
                    applied_at  TEXT NOT NULL DEFAULT (datetime('now'))
                );
                """,
            _ => throw new NotSupportedException($"Database type {_databaseType} is not supported")
        };

        await _connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    public async Task<bool> IsAppliedAsync(string migrationId)
    {
        var sql = _databaseType == DatabaseType.SqlServer
            ? "SELECT COUNT(1) FROM [dbo].[SchemaMigrations] WHERE [Id] = @Id"
            : "SELECT COUNT(1) FROM schema_migrations WHERE id = @Id";

        var count = await _connection.QuerySingleAsync<int>(sql, new { Id = migrationId }).ConfigureAwait(false);
        return count > 0;
    }

    public async Task<IReadOnlyList<string>> GetAppliedIdsAsync()
    {
        var sql = _databaseType == DatabaseType.SqlServer
            ? "SELECT [Id] FROM [dbo].[SchemaMigrations] ORDER BY [AppliedAt]"
            : "SELECT id FROM schema_migrations ORDER BY applied_at";

        var ids = await _connection.QueryAsync<string>(sql).ConfigureAwait(false);
        return ids.ToList();
    }

    private async Task MarkAppliedAsync(string migrationId, string description)
    {
        var sql = _databaseType == DatabaseType.SqlServer
            ? "INSERT INTO [dbo].[SchemaMigrations] ([Id], [Description]) VALUES (@Id, @Description)"
            : "INSERT INTO schema_migrations (id, description) VALUES (@Id, @Description)";

        await _connection.ExecuteAsync(sql, new { Id = migrationId, Description = description }).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs every pending migration in the order supplied, journaling each
    /// as it completes. Already-applied migrations are skipped. Returns the
    /// ids that ran this call (empty when the schema was already current).
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(IEnumerable<SchemaMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        await EnsureJournalTableAsync().ConfigureAwait(false);

        var applied = new List<string>();
        foreach (var migration in migrations)
        {
            if (await IsAppliedAsync(migration.Id).ConfigureAwait(false))
            {
                continue;
            }

            // Run the (idempotent) work first, then journal it. If the
            // process dies between the two, the step re-runs harmlessly
            // next boot. See the class remarks for why this is not wrapped
            // in a transaction.
            await migration.ApplyAsync(_connection).ConfigureAwait(false);
            await MarkAppliedAsync(migration.Id, migration.Description).ConfigureAwait(false);
            applied.Add(migration.Id);
        }

        return applied;
    }
}
