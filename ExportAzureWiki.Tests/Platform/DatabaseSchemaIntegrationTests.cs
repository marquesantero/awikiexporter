using System.Diagnostics;
using ExportAzureWiki.Data;
using ExportAzureWiki.Data.Schema;
using ExportAzureWiki.Models;
using ExportAzureWiki.Tests.Security;
using Microsoft.Win32;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Real-engine integration tests: spin up SQL Server / PostgreSQL / MySQL in a
/// container, then exercise the exact path that broke before — create the
/// database, create the schema, and seed the default OAuth providers (the
/// ClientId NOT NULL regression). Skipped (no-op) when Docker is unavailable,
/// so they do not fail on runners without a Linux Docker daemon.
/// </summary>
[Collection(TempKeyCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DatabaseSchemaIntegrationTests
{
    [Fact]
    public async Task SqlServer_Creates_Database_Schema_And_Seeds_Providers()
    {
        if (!DockerAvailable())
        {
            return;
        }

        await using var container = new MsSqlBuilder()
            .WithPassword("Str0ng(!)Passw0rd")
            .Build();
        await container.StartAsync();

        await RunSchemaFlowAsync(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.SqlServer,
            Server = container.Hostname,
            Port = container.GetMappedPublicPort(1433),
            Username = "sa",
            Password = "Str0ng(!)Passw0rd",
            Database = "awiki_test",
            TrustServerCertificate = true,
        });
    }

    [Fact]
    public async Task MySql_Creates_Database_Schema_And_Seeds_Providers()
    {
        if (!DockerAvailable())
        {
            return;
        }

        // MySQL 8 generates a self-signed server certificate and enables TLS by
        // default, so TrustServerCertificate honors the app's strict-TLS policy.
        await using var container = new MySqlBuilder()
            .WithUsername("root")
            .WithPassword("rootpass")
            .Build();
        await container.StartAsync();

        await RunSchemaFlowAsync(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.MySQL,
            Server = container.Hostname,
            Port = container.GetMappedPublicPort(3306),
            Username = "root",
            Password = "rootpass",
            Database = "awiki_test",
            TrustServerCertificate = true,
        });
    }

    // NOTE: PostgreSQL is intentionally not covered here. The app enforces TLS
    // (SslMode Require/VerifyFull) and the stock `postgres` image ships with SSL
    // disabled, so even the lab opt-in cannot connect. A PostgreSQL integration
    // test needs an SSL-enabled container (server cert/key mounted) -- follow-up.

    private static async Task RunSchemaFlowAsync(DatabaseConfiguration config)
    {
        var keyPath = $@"Software\ExportAzureWiki.Tests\{Guid.NewGuid():N}";
        DbConnectionFactory.RegistryKeyPathOverride = keyPath;
        try
        {
            var factory = new DbConnectionFactory();
            factory.SetConnectionFromConfig(config);
            var schema = new SchemaManager(factory);

            (await schema.DatabaseExistsAsync()).Should().BeFalse("the database is created by the test");
            (await schema.CreateDatabaseAsync()).Should().BeTrue();
            (await schema.DatabaseExistsAsync()).Should().BeTrue();
            (await schema.CreateSchemaAsync()).Should().BeTrue();
            (await schema.SchemaExistsAsync()).Should().BeTrue();
            (await schema.SeedOAuthProvidersAsync()).Should().BeTrue();

            // The seed must have inserted the 4 default providers; this is the
            // ClientId NOT NULL regression that previously failed silently.
            using var connection = await factory.CreateConnectionAsync();
            var table = config.DatabaseType == DatabaseType.SqlServer ? "[dbo].[OAuthProviders]" : "oauth_providers";
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            var count = Convert.ToInt32(command.ExecuteScalar());
            count.Should().Be(4);
        }
        finally
        {
            DbConnectionFactory.RegistryKeyPathOverride = null;
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ExportAzureWiki.Tests", throwOnMissingSubKey: false); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static bool DockerAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null)
            {
                return false;
            }

            return process.WaitForExit(8000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
