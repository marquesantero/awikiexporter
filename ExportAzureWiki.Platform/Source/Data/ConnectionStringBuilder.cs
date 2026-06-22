using ExportAzureWiki.Models;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace ExportAzureWiki.Data;

/// <summary>
/// Builds connection strings for different database types
/// </summary>
public static class ConnectionStringBuilder
{
    /// <summary>
    /// Builds a connection string from the database configuration
    /// </summary>
    /// <param name="config">Database configuration</param>
    /// <returns>Connection string</returns>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    public static string BuildConnectionString(DatabaseConfiguration config)
    {
        return config.DatabaseType switch
        {
            DatabaseType.SqlServer => BuildSqlServerConnectionString(config),
            DatabaseType.PostgreSQL => BuildPostgreSqlConnectionString(config),
            DatabaseType.MySQL => BuildMySqlConnectionString(config),
            DatabaseType.SQLite => BuildSQLiteConnectionString(config),
            _ => throw new ArgumentException($"Unsupported database type: {config.DatabaseType}")
        };
    }

    /// <summary>
    /// Validates that the database configuration is complete
    /// </summary>
    /// <param name="config">Database configuration</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool ValidateConfiguration(DatabaseConfiguration config)
    {
        return config.DatabaseType switch
        {
            DatabaseType.SqlServer => ValidateSqlServer(config),
            DatabaseType.PostgreSQL => ValidatePostgreSql(config),
            DatabaseType.MySQL => ValidateMySql(config),
            DatabaseType.SQLite => ValidateSQLite(config),
            _ => false
        };
    }

    private static string BuildSqlServerConnectionString(DatabaseConfiguration config)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = config.Port > 0 ? $"{config.Server},{config.Port}" : config.Server,
            InitialCatalog = config.Database,
            // Strict TLS by default. TrustServerCertificate must be an explicit
            // opt-in via DatabaseConfiguration; a default of true silently
            // accepts MitM attempts against on-prem SQL Server. See Fase 1.3.
            Encrypt = true,
            TrustServerCertificate = config.TrustServerCertificate,
            ConnectTimeout = 30,
            ApplicationName = "ExportAzureWiki"
        };

        if (config.UseWindowsAuth)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = config.Username;
            builder.Password = config.Password;
        }

        return builder.ConnectionString;
    }

    private static string BuildPostgreSqlConnectionString(DatabaseConfiguration config)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = config.Server,
            Port = config.Port > 0 ? config.Port : 5432,
            Database = config.Database,
            Username = config.Username ?? string.Empty,
            Password = config.Password ?? string.Empty,
            Timeout = 30,
            ApplicationName = "ExportAzureWiki",
            // SslMode.Prefer would fall back to plaintext on servers without
            // TLS configured; that defeats the point on a corporate network.
            // Require enforces TLS; VerifyFull would also validate the host
            // name but trips up on local/dev certs without further config.
            // TrustServerCertificate opts back into Require-without-validation
            // for lab environments only.
            SslMode = config.TrustServerCertificate ? SslMode.Require : SslMode.VerifyFull
        };

        return builder.ConnectionString;
    }

    private static string BuildMySqlConnectionString(DatabaseConfiguration config)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = config.Server,
            Port = (uint)(config.Port > 0 ? config.Port : 3306),
            Database = config.Database,
            UserID = config.Username ?? string.Empty,
            Password = config.Password ?? string.Empty,
            ConnectionTimeout = 30,
            AllowUserVariables = true,
            UseAffectedRows = false,
            // Required: TLS mandatory, but certificate not validated against
            // CA chain. VerifyCA / VerifyFull validate the chain and host but
            // need server-side cert config beyond what the wizard collects.
            // Match Postgres semantics: Required by default; VerifyCA when
            // the user has not opted into trusting self-signed certs.
            SslMode = config.TrustServerCertificate
                ? MySqlSslMode.Required
                : MySqlSslMode.VerifyCA
        };

        return builder.ConnectionString;
    }

    private static string BuildSQLiteConnectionString(DatabaseConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.FilePath))
        {
            throw new ArgumentException("SQLite file path is required");
        }

        return $"Data Source={config.FilePath};Cache=Shared;Foreign Keys=True;";
    }

    private static bool ValidateSqlServer(DatabaseConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Server))
            return false;

        if (string.IsNullOrWhiteSpace(config.Database))
            return false;

        if (!config.UseWindowsAuth)
        {
            if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
                return false;
        }

        return true;
    }

    private static bool ValidatePostgreSql(DatabaseConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.Server) &&
               !string.IsNullOrWhiteSpace(config.Database) &&
               !string.IsNullOrWhiteSpace(config.Username) &&
               !string.IsNullOrWhiteSpace(config.Password);
    }

    private static bool ValidateMySql(DatabaseConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.Server) &&
               !string.IsNullOrWhiteSpace(config.Database) &&
               !string.IsNullOrWhiteSpace(config.Username) &&
               !string.IsNullOrWhiteSpace(config.Password);
    }

    private static bool ValidateSQLite(DatabaseConfiguration config)
    {
        return !string.IsNullOrWhiteSpace(config.FilePath);
    }
}
