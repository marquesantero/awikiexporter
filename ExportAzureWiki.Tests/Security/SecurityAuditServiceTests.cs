using System.Data;
using System.Data.Common;
using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Data;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services.Authentication;
using Microsoft.Data.Sqlite;

namespace ExportAzureWiki.Tests.Security;

/// <summary>
/// End-to-end-ish coverage of SecurityAuditService against an in-memory
/// SQLite database. Verifies the table layout, the insert path, and the
/// reader path; the SQLite branch is the one the dev workstation default
/// uses, so it's the highest-impact dialect to lock down.
/// </summary>
public sealed class SecurityAuditServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestConnectionFactory _factory;
    private readonly SecurityAuditService _service;

    public SecurityAuditServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "ExportAzureWiki.Tests.audit-" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new TestConnectionFactory(_dbPath);
        CreateTable(_factory);
        _service = new SecurityAuditService(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Record_Then_List_Returns_The_Event()
    {
        await _service.RecordAsync(SecurityAuditEventTypes.LoginSuccess, userId: 42, username: "alice");

        var entries = await _service.ListRecentAsync(max: 10);

        entries.Should().ContainSingle();
        entries[0].EventType.Should().Be(SecurityAuditEventTypes.LoginSuccess);
        entries[0].UserId.Should().Be(42);
        entries[0].Username.Should().Be("alice");
    }

    [Fact]
    public async Task Record_With_Object_Detail_Serializes_To_Json()
    {
        await _service.RecordAsync(
            SecurityAuditEventTypes.LoginFailure,
            userId: null,
            username: "intruder",
            detail: new { reason = "unknown_user", attempt = 3 });

        var entry = (await _service.ListRecentAsync()).Single();

        entry.Detail.Should().Contain("unknown_user");
        entry.Detail.Should().Contain("3");
    }

    [Fact]
    public async Task ListRecent_Caps_The_Result_Set()
    {
        for (var i = 0; i < 25; i++)
        {
            await _service.RecordAsync(SecurityAuditEventTypes.LoginSuccess, i, $"u{i}");
        }

        var entries = await _service.ListRecentAsync(max: 5);

        entries.Should().HaveCount(5);
    }

    [Fact]
    public async Task ListRecent_Returns_Newest_First()
    {
        await _service.RecordAsync(SecurityAuditEventTypes.LoginSuccess, 1, "first");
        await Task.Delay(20);
        await _service.RecordAsync(SecurityAuditEventTypes.LoginSuccess, 2, "second");

        var entries = await _service.ListRecentAsync();

        entries.Should().HaveCount(2);
        entries[0].Username.Should().Be("second");
        entries[1].Username.Should().Be("first");
    }

    [Fact]
    public async Task ListRecent_With_Zero_Max_Returns_Empty()
    {
        await _service.RecordAsync(SecurityAuditEventTypes.LoginSuccess, 1, "alice");
        var entries = await _service.ListRecentAsync(max: 0);
        entries.Should().BeEmpty();
    }

    private static void CreateTable(TestConnectionFactory factory)
    {
        using var connection = (SqliteConnection)factory.CreateConnectionAsync().GetAwaiter().GetResult();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS security_audit_log (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                occurred_at TEXT NOT NULL DEFAULT (datetime('now')),
                event_type  TEXT NOT NULL,
                user_id     INTEGER NULL,
                username    TEXT NULL,
                ip_address  TEXT NULL,
                user_agent  TEXT NULL,
                detail      TEXT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    private sealed class TestConnectionFactory : IDbConnectionFactory
    {
        private readonly string _path;

        public TestConnectionFactory(string path) => _path = path;

        public Task<IDbConnection> CreateConnectionAsync()
        {
            var connection = new SqliteConnection($"Data Source={_path};Cache=Shared");
            connection.Open();
            return Task.FromResult<IDbConnection>(connection);
        }

        public Task<bool> TestConnectionAsync() => Task.FromResult(true);
        public DatabaseType GetDatabaseType() => DatabaseType.SQLite;
        public string GetConnectionString() => $"Data Source={_path}";
        public DatabaseConfiguration? LoadConfiguration() => null;
        public void SetConnectionFromConfig(DatabaseConfiguration config) { }
        public void SetConnection(string connectionString, DatabaseType databaseType) { }
    }
}
