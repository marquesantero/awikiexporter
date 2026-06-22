using System.Data;
using ExportAzureWiki.Data;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services;
using ExportAzureWiki.Tests.Security;
using Microsoft.Data.Sqlite;

namespace ExportAzureWiki.Tests.Platform;

[Collection(TempKeyCollection.Name)]
public sealed class WikiConfigurationStorageServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"awikiexporter-tests-{Guid.NewGuid():N}.db");
    private readonly TestConnectionFactory _factory;

    public WikiConfigurationStorageServiceTests()
    {
        _factory = new TestConnectionFactory(_dbPath);
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE wiki_configurations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                organization TEXT NOT NULL,
                project TEXT NOT NULL,
                wiki_identifier TEXT NOT NULL,
                personal_access_token TEXT NOT NULL,
                platform INTEGER NOT NULL DEFAULT 0,
                auth_type INTEGER NOT NULL DEFAULT 0,
                authentication_data_json TEXT,
                platform_specific_data_json TEXT,
                is_active INTEGER NOT NULL DEFAULT 1,
                icon_color TEXT,
                is_default INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                last_used_at TEXT,
                last_modified_at TEXT,
                owner_user_id TEXT,
                owner_display_name TEXT,
                visibility_scope TEXT NOT NULL DEFAULT 'Global',
                created_by_admin INTEGER NOT NULL DEFAULT 0,
                root_path TEXT,
                UNIQUE(organization, project, wiki_identifier)
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_dbPath);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    [Fact]
    public void SaveAll_LoadAll_Preserves_GitHub_Configuration_Fields()
    {
        var service = new WikiConfigurationStorageService(_factory);
        var source = new WikiConfiguration
        {
            Name = "Product Docs",
            Platform = WikiPlatform.GitHub,
            BaseUrl = "https://github.com",
            AuthType = AuthenticationType.PersonalAccessToken,
            RootPath = "docs/getting-started.md",
            IsDefault = true,
            IsActive = true,
            IconColor = "#24292e",
            OwnerUserId = "1",
            OwnerDisplayName = "Admin",
            VisibilityScope = WikiVisibilityScope.Global,
            CreatedByAdmin = true,
            AuthenticationData = new Dictionary<string, string>
            {
                ["Token"] = "ghp_test_token"
            },
            PlatformSpecificData = new Dictionary<string, string>
            {
                ["Owner"] = "marquesantero",
                ["Repository"] = "awikiexporter",
                ["Mode"] = "Repo",
                ["Branch"] = "main",
                ["DocsPath"] = "docs"
            }
        };

        service.SaveAll([source]);

        var loaded = service.LoadAll().Should().ContainSingle().Subject;
        loaded.Name.Should().Be("Product Docs");
        loaded.Platform.Should().Be(WikiPlatform.GitHub);
        loaded.AuthType.Should().Be(AuthenticationType.PersonalAccessToken);
        loaded.BaseUrl.Should().Be("https://github.com");
        loaded.RootPath.Should().Be("docs/getting-started.md");
        loaded.IsActive.Should().BeTrue();
        loaded.IconColor.Should().Be("#24292e");
        loaded.AuthenticationData.Should().Contain("Token", "ghp_test_token");
        loaded.PlatformSpecificData.Should().Contain("Owner", "marquesantero");
        loaded.PlatformSpecificData.Should().Contain("Repository", "awikiexporter");
        loaded.PlatformSpecificData.Should().Contain("Mode", "Repo");
        loaded.PlatformSpecificData.Should().Contain("Branch", "main");
        loaded.PlatformSpecificData.Should().Contain("DocsPath", "docs");

        ReadAuthenticationJson().Should().NotContain("ghp_test_token");
    }

    private SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private string ReadAuthenticationJson()
    {
        using var connection = CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT authentication_data_json FROM wiki_configurations LIMIT 1";
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private sealed class TestConnectionFactory : IDbConnectionFactory
    {
        private readonly string _path;

        public TestConnectionFactory(string path)
        {
            _path = path;
        }

        public async Task<IDbConnection> CreateConnectionAsync()
        {
            var connection = new SqliteConnection($"Data Source={_path}");
            await connection.OpenAsync();
            return connection;
        }

        public Task<bool> TestConnectionAsync() => Task.FromResult(true);
        public DatabaseType GetDatabaseType() => DatabaseType.SQLite;
        public string GetConnectionString() => $"Data Source={_path}";
        public void SetConnection(string connectionString, DatabaseType databaseType) { }
        public void SetConnectionFromConfig(DatabaseConfiguration config) { }
        public DatabaseConfiguration? LoadConfiguration() => null;
    }
}
