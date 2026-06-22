using ExportAzureWiki.Data;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Tests.Security;

public sealed class ConnectionStringBuilderTests
{
    [Fact]
    public void SqlServer_Default_Enforces_Encrypt_And_Validates_Certificate()
    {
        var cs = ConnectionStringBuilder.BuildConnectionString(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.SqlServer,
            Server = "tcp:db.contoso.com",
            Database = "wiki",
            Username = "app",
            Password = "p@ss",
        });

        cs.Should().Contain("Encrypt=True", "TLS must be on by default");
        cs.Should().Contain("Trust Server Certificate=False",
            "the cert must validate against the system trust store by default");
    }

    [Fact]
    public void SqlServer_With_Trust_Override_Sets_Trust_True_But_Keeps_Encryption_On()
    {
        var cs = ConnectionStringBuilder.BuildConnectionString(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.SqlServer,
            Server = "db.local",
            Database = "wiki",
            Username = "app",
            Password = "p@ss",
            TrustServerCertificate = true
        });

        cs.Should().Contain("Encrypt=True");
        cs.Should().Contain("Trust Server Certificate=True");
    }

    [Fact]
    public void PostgreSql_Default_Uses_VerifyFull()
    {
        var cs = ConnectionStringBuilder.BuildConnectionString(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.PostgreSQL,
            Server = "pg.contoso.com",
            Database = "wiki",
            Username = "app",
            Password = "p@ss"
        });

        cs.Should().Contain("SSL Mode=VerifyFull");
    }

    [Fact]
    public void PostgreSql_Trust_Override_Falls_Back_To_Require()
    {
        var cs = ConnectionStringBuilder.BuildConnectionString(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.PostgreSQL,
            Server = "pg.local",
            Database = "wiki",
            Username = "app",
            Password = "p@ss",
            TrustServerCertificate = true
        });

        cs.Should().Contain("SSL Mode=Require");
    }

    [Fact]
    public void MySql_Default_Uses_VerifyCA()
    {
        var cs = ConnectionStringBuilder.BuildConnectionString(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.MySQL,
            Server = "mysql.contoso.com",
            Database = "wiki",
            Username = "app",
            Password = "p@ss"
        });

        // MySqlConnector formats the setting as "SSL Mode" or "SslMode" depending
        // on version. Either way, "VerifyCA" should be the value.
        cs.Should().Contain("VerifyCA");
    }

    [Fact]
    public void Sqlite_Connection_Has_No_Network_Knobs()
    {
        var cs = ConnectionStringBuilder.BuildConnectionString(new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.SQLite,
            FilePath = "C:\\temp\\wiki.db"
        });

        cs.Should().Contain("Data Source=").And.Contain("wiki.db");
        cs.Should().NotContain("SslMode");
        cs.Should().NotContain("Encrypt");
    }
}
