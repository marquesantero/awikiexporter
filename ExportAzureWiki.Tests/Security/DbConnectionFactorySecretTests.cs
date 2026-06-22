using ExportAzureWiki.Data;
using ExportAzureWiki.Models;
using Microsoft.Win32;

namespace ExportAzureWiki.Tests.Security;

/// <summary>
/// Proves the database connection string (which can carry the DB password)
/// is encrypted before it is written to the registry, and round-trips back
/// in clear. Uses a throwaway HKCU subkey, removed on dispose.
/// </summary>
[Collection(TempKeyCollection.Name)]
public sealed class DbConnectionFactorySecretTests : IDisposable
{
    private readonly string _testKeyPath;

    public DbConnectionFactorySecretTests()
    {
        _testKeyPath = $@"Software\ExportAzureWiki.Tests\{Guid.NewGuid():N}";
        DbConnectionFactory.RegistryKeyPathOverride = _testKeyPath;
    }

    public void Dispose()
    {
        DbConnectionFactory.RegistryKeyPathOverride = null;
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ExportAzureWiki.Tests", throwOnMissingSubKey: false); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Connection_String_Is_Encrypted_In_The_Registry()
    {
        const string conn = "Server=db.contoso.com;Database=wiki;User Id=app;Password=Sup3rSecret!;";

        new DbConnectionFactory().SetConnection(conn, DatabaseType.SqlServer);

        using var key = Registry.CurrentUser.OpenSubKey(_testKeyPath);
        var raw = key!.GetValue("ConnectionString") as string;

        raw.Should().StartWith("enc:");
        raw.Should().NotContain("Sup3rSecret!", "the password must not be stored in clear");
        raw.Should().NotContain("db.contoso.com");
    }

    [Fact]
    public void Round_Trips_The_Connection_String_And_Type()
    {
        const string conn = "Server=db.contoso.com;Database=wiki;User Id=app;Password=Sup3rSecret!;";

        new DbConnectionFactory().SetConnection(conn, DatabaseType.SqlServer);

        // A fresh factory loads + decrypts from the same subkey.
        var reloaded = new DbConnectionFactory();
        reloaded.GetConnectionString().Should().Be(conn);
        reloaded.GetDatabaseType().Should().Be(DatabaseType.SqlServer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrustServerCertificate_RoundTrips_Through_LoadConfiguration(bool trust)
    {
        var config = new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.SqlServer,
            Server = "db.contoso.com",
            Database = "wiki",
            Username = "app",
            Password = "Sup3rSecret!",
            TrustServerCertificate = trust
        };

        new DbConnectionFactory().SetConnectionFromConfig(config);

        var reloaded = new DbConnectionFactory().LoadConfiguration();
        reloaded.Should().NotBeNull();
        reloaded!.TrustServerCertificate.Should().Be(trust);
    }

    [Fact]
    public void Legacy_Plaintext_Connection_String_Still_Loads()
    {
        const string conn = "Data Source=C:/data/wiki.db";
        // Simulate a pre-encryption install: raw plaintext in the registry.
        using (var key = Registry.CurrentUser.CreateSubKey(_testKeyPath))
        {
            key.SetValue("ConnectionString", conn);
            key.SetValue("DatabaseType", DatabaseType.SQLite.ToString());
        }

        new DbConnectionFactory().GetConnectionString().Should().Be(conn);
    }
}
