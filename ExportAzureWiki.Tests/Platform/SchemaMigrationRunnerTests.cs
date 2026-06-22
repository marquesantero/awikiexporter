using System.Data;
using Dapper;
using ExportAzureWiki.Data;
using ExportAzureWiki.Data.Schema;
using Microsoft.Data.Sqlite;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Exercises the migration journal against a real in-memory SQLite
/// connection. SQLite is the dev-workstation default and the only engine
/// available without containers, so it's the one we can fully verify here.
/// </summary>
public sealed class SchemaMigrationRunnerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SchemaMigrationRunnerTests()
    {
        // A single shared in-memory connection: the database lives as long
        // as the connection is open.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private SchemaMigrationRunner CreateRunner() => new(_connection, DatabaseType.SQLite);

    [Fact]
    public async Task EnsureJournalTable_Is_Idempotent()
    {
        var runner = CreateRunner();
        await runner.EnsureJournalTableAsync();
        // Second call must not throw.
        await runner.EnsureJournalTableAsync();

        var count = await _connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations'");
        count.Should().Be(1);
    }

    [Fact]
    public async Task Run_Applies_Pending_Migrations_In_Order()
    {
        var order = new List<string>();
        var runner = CreateRunner();

        var applied = await runner.RunAsync(new[]
        {
            new SchemaMigration("0001_a", "first", _ => { order.Add("a"); return Task.CompletedTask; }),
            new SchemaMigration("0002_b", "second", _ => { order.Add("b"); return Task.CompletedTask; }),
        });

        applied.Should().Equal("0001_a", "0002_b");
        order.Should().Equal("a", "b");
    }

    [Fact]
    public async Task Run_Skips_Already_Applied_Migrations()
    {
        var runner = CreateRunner();
        var runs = 0;

        var migration = new SchemaMigration("0001_once", "runs once", _ => { runs++; return Task.CompletedTask; });

        await runner.RunAsync(new[] { migration });
        await runner.RunAsync(new[] { migration });

        runs.Should().Be(1, "an applied migration must not run a second time");
    }

    [Fact]
    public async Task Run_Applies_Only_New_Migrations_On_Second_Pass()
    {
        var runner = CreateRunner();
        var firstRuns = 0;
        var secondRuns = 0;

        await runner.RunAsync(new[]
        {
            new SchemaMigration("0001_old", "old", _ => { firstRuns++; return Task.CompletedTask; }),
        });

        var appliedSecondPass = await runner.RunAsync(new[]
        {
            new SchemaMigration("0001_old", "old", _ => { firstRuns++; return Task.CompletedTask; }),
            new SchemaMigration("0002_new", "new", _ => { secondRuns++; return Task.CompletedTask; }),
        });

        firstRuns.Should().Be(1, "the old migration is journaled and skipped");
        secondRuns.Should().Be(1, "the new migration runs once");
        appliedSecondPass.Should().Equal("0002_new");
    }

    [Fact]
    public async Task GetAppliedIds_Returns_Everything_That_Ran()
    {
        var runner = CreateRunner();
        await runner.RunAsync(new[]
        {
            new SchemaMigration("0001_a", "a", _ => Task.CompletedTask),
            new SchemaMigration("0002_b", "b", _ => Task.CompletedTask),
        });

        var ids = await runner.GetAppliedIdsAsync();
        ids.Should().Contain(new[] { "0001_a", "0002_b" });
    }

    [Fact]
    public async Task Failed_Migration_Is_Not_Journaled_And_Reruns()
    {
        var runner = CreateRunner();
        var attempts = 0;

        var flaky = new SchemaMigration("0001_flaky", "fails first time", _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("boom");
            }
            return Task.CompletedTask;
        });

        // First pass throws.
        var act = async () => await runner.RunAsync(new[] { flaky });
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await runner.IsAppliedAsync("0001_flaky")).Should().BeFalse("a failed migration must not be journaled");

        // Second pass succeeds and journals.
        await runner.RunAsync(new[] { flaky });
        (await runner.IsAppliedAsync("0001_flaky")).Should().BeTrue();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task Migration_Work_Persists_To_The_Database()
    {
        var runner = CreateRunner();
        await runner.RunAsync(new[]
        {
            new SchemaMigration("0001_make_table", "creates a table", async c =>
            {
                await c.ExecuteAsync("CREATE TABLE demo (id INTEGER PRIMARY KEY, name TEXT)");
                await c.ExecuteAsync("INSERT INTO demo (name) VALUES ('hello')");
            }),
        });

        var name = await _connection.QuerySingleAsync<string>("SELECT name FROM demo LIMIT 1");
        name.Should().Be("hello");
    }
}
