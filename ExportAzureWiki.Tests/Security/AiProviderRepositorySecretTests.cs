using System.Data;
using Dapper;
using ExportAzureWiki.Data;
using ExportAzureWiki.Data.Repositories;
using ExportAzureWiki.Models;
using Microsoft.Data.Sqlite;

namespace ExportAzureWiki.Tests.Security;

/// <summary>
/// Proves the AI provider API key is encrypted at rest: written as enc:...
/// and read back in clear. Runs against an in-memory SQLite database with
/// the real snake_case schema the repository introspects.
/// </summary>
[Collection(TempKeyCollection.Name)]
public sealed class AiProviderRepositorySecretTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AiProviderRepositorySecretTests()
    {
        // The production startup sets this globally (snake_case <-> PascalCase).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE ai_providers (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_name      TEXT NOT NULL,
                display_name       TEXT NOT NULL,
                is_enabled         INTEGER NOT NULL DEFAULT 0,
                is_default         INTEGER NOT NULL DEFAULT 0,
                endpoint_url       TEXT NULL,
                api_key            TEXT NULL,
                model_name         TEXT NULL,
                api_version        TEXT NULL,
                organization_id    TEXT NULL,
                configuration_json TEXT NULL,
                created_at         TEXT NULL,
                last_modified_at   TEXT NULL
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    private AiProviderRepository NewRepo() => new(_connection, DatabaseType.SQLite);

    [Fact]
    public async Task ApiKey_Is_Stored_Encrypted_And_Read_Back_Clear()
    {
        const string secret = "sk-super-secret-ai-key-12345";

        var id = await NewRepo().AddAsync(new AiProvider
        {
            ProviderName = "openai",
            DisplayName = "OpenAI",
            IsEnabled = true,
            ApiKey = secret,
            CreatedAt = DateTime.UtcNow,
        });

        // Raw column must NOT contain the plaintext, and must carry the marker.
        var raw = await _connection.QuerySingleAsync<string>(
            "SELECT api_key FROM ai_providers WHERE id = @id", new { id });
        raw.Should().StartWith("enc:");
        raw.Should().NotContain(secret);

        // Reading through the repository reveals the clear value.
        var loaded = await NewRepo().GetByIdAsync(id);
        loaded.Should().NotBeNull();
        loaded!.ApiKey.Should().Be(secret);
    }

    [Fact]
    public async Task GetByProviderName_And_Enabled_Reveal_The_Key()
    {
        const string secret = "sk-enabled-key";
        await NewRepo().AddAsync(new AiProvider
        {
            ProviderName = "azure-openai",
            DisplayName = "Azure OpenAI",
            IsEnabled = true,
            ApiKey = secret,
            CreatedAt = DateTime.UtcNow,
        });

        (await NewRepo().GetByProviderNameAsync("azure-openai"))!.ApiKey.Should().Be(secret);
        (await NewRepo().GetEnabledProvidersAsync()).Single().ApiKey.Should().Be(secret);
    }

    [Fact]
    public async Task Legacy_Plaintext_Key_Is_Read_As_Is()
    {
        // Row written before Fase 1.6 (no enc: prefix).
        await _connection.ExecuteAsync("""
            INSERT INTO ai_providers (provider_name, display_name, is_enabled, api_key, created_at)
            VALUES ('legacy', 'Legacy', 1, 'plaintext-legacy-key', @now);
            """, new { now = DateTime.UtcNow.ToString("o") });

        var loaded = await NewRepo().GetByProviderNameAsync("legacy");
        loaded!.ApiKey.Should().Be("plaintext-legacy-key");
    }
}
