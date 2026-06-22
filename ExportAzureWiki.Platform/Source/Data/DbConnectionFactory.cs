using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using MySqlConnector;
using Npgsql;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Data;

/// <summary>
/// Factory for creating database connections
/// </summary>
public class DbConnectionFactory : IDbConnectionFactory
{
    private const string DefaultRegistryKeyPath = @"Software\ExportAzureWiki";
    private const string ConnectionStringValueName = "ConnectionString";
    private const string DatabaseTypeValueName = "DatabaseType";

    /// <summary>
    /// Test seam: overrides the HKCU subkey used to persist the connection.
    /// Production code leaves this null and uses
    /// <see cref="DefaultRegistryKeyPath"/>.
    /// </summary>
    internal static string? RegistryKeyPathOverride { get; set; }

    private static string RegistryKeyPath => RegistryKeyPathOverride ?? DefaultRegistryKeyPath;

    private string? _connectionString;
    private DatabaseType _databaseType;

    public DbConnectionFactory()
    {
        LoadConnection();
    }

    /// <inheritdoc/>
    public async Task<IDbConnection> CreateConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("Database connection has not been configured. Please run the setup wizard.");
        }

        IDbConnection connection;

        // ConfigureAwait(false) is critical: a lot of the data layer is
        // sync-over-async (CreateConnectionAsync().GetAwaiter().GetResult()).
        // Without it, the OpenAsync continuation would marshal back to the
        // calling SynchronizationContext; on the WPF UI thread that deadlocks
        // against a real-async provider (SQL Server/PostgreSQL/MySQL). SQLite
        // happened to work only because it completes synchronously.
        switch (_databaseType)
        {
            case DatabaseType.SqlServer:
                var sqlConn = new SqlConnection(_connectionString);
                await sqlConn.OpenAsync().ConfigureAwait(false);
                connection = sqlConn;
                break;
            case DatabaseType.PostgreSQL:
                var npgsqlConn = new NpgsqlConnection(_connectionString);
                await npgsqlConn.OpenAsync().ConfigureAwait(false);
                connection = npgsqlConn;
                break;
            case DatabaseType.MySQL:
                var mysqlConn = new MySqlConnection(_connectionString);
                await mysqlConn.OpenAsync().ConfigureAwait(false);
                connection = mysqlConn;
                break;
            case DatabaseType.SQLite:
                var sqliteConn = new SqliteConnection(_connectionString);
                await sqliteConn.OpenAsync().ConfigureAwait(false);
                connection = sqliteConn;
                break;
            default:
                throw new NotSupportedException($"Database type {_databaseType} is not supported");
        }

        return connection;
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = await CreateConnectionAsync().ConfigureAwait(false);
            return connection.State == ConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public DatabaseType GetDatabaseType()
    {
        return _databaseType;
    }

    /// <inheritdoc/>
    public string GetConnectionString()
    {
        return _connectionString ?? string.Empty;
    }

    /// <inheritdoc/>
    public void SetConnection(string connectionString, DatabaseType databaseType)
    {
        _connectionString = connectionString;
        _databaseType = databaseType;
        SaveConnectionToRegistry();
    }

    /// <summary>
    /// Sets connection from configuration object and saves to file
    /// </summary>
    public void SetConnectionFromConfig(DatabaseConfiguration config)
    {
        var connectionString = ConnectionStringBuilder.BuildConnectionString(config);
        _connectionString = connectionString;
        _databaseType = config.DatabaseType;

        SaveConnectionToRegistry();
    }

    /// <summary>
    /// Loads the current database configuration
    /// </summary>
    public DatabaseConfiguration? LoadConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return null;
        }

        return BuildConfigurationFromCurrentConnection();
    }

    private void LoadConnection()
    {
        LoadConnectionFromRegistry();
    }

    private void LoadConnectionFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key != null)
            {
                // Reveal the AES-GCM-protected value. Legacy plaintext rows
                // (written before this change) pass through unchanged and are
                // re-encrypted on the next SetConnection. A decryption failure
                // (e.g. a different Windows user / rotated key) yields empty,
                // which surfaces as "not configured" and re-runs setup.
                _connectionString = StoredSecret.Reveal(key.GetValue(ConnectionStringValueName) as string);

                var dbTypeValue = key.GetValue(DatabaseTypeValueName) as string;
                if (!string.IsNullOrEmpty(dbTypeValue) && Enum.TryParse<DatabaseType>(dbTypeValue, out var dbType))
                {
                    _databaseType = dbType;
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't throw - this is expected on first run
            System.Diagnostics.Debug.WriteLine($"Error loading connection from registry: {ex.Message}");
        }
    }

    private void SaveConnectionToRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            // The connection string can carry the database password (SQL
            // Server / MySQL / PostgreSQL). Encrypt it at rest with AES-GCM
            // under the per-user DPAPI master key before it touches the
            // registry, so a registry/backup read does not leak credentials.
            var protectedConnectionString = StoredSecret.Protect(_connectionString);
            key.SetValue(ConnectionStringValueName, protectedConnectionString);
            key.SetValue(DatabaseTypeValueName, _databaseType.ToString());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to save database configuration to registry", ex);
        }
    }

    private DatabaseConfiguration? BuildConfigurationFromCurrentConnection()
    {
        try
        {
            return _databaseType switch
            {
                DatabaseType.SqlServer => BuildSqlServerConfiguration(_connectionString!),
                DatabaseType.PostgreSQL => BuildPostgreSqlConfiguration(_connectionString!),
                DatabaseType.MySQL => BuildMySqlConfiguration(_connectionString!),
                DatabaseType.SQLite => BuildSqliteConfiguration(_connectionString!),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static DatabaseConfiguration BuildSqlServerConfiguration(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.SqlServer,
            Server = builder.DataSource,
            Database = builder.InitialCatalog,
            Username = builder.UserID,
            Password = builder.Password,
            UseWindowsAuth = builder.IntegratedSecurity,
            TrustServerCertificate = builder.TrustServerCertificate
        };
    }

    private static DatabaseConfiguration BuildPostgreSqlConfiguration(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.PostgreSQL,
            Server = builder.Host ?? string.Empty,
            Port = builder.Port,
            Database = builder.Database ?? string.Empty,
            Username = builder.Username,
            Password = builder.Password,
            UseWindowsAuth = false,
            // ConnectionStringBuilder maps TrustServerCertificate -> SslMode.Require
            // (vs VerifyFull); reverse it so the setting round-trips.
            TrustServerCertificate = builder.SslMode == Npgsql.SslMode.Require
        };
    }

    private static DatabaseConfiguration BuildMySqlConfiguration(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        return new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.MySQL,
            Server = builder.Server,
            Port = (int)builder.Port,
            Database = builder.Database,
            Username = builder.UserID,
            Password = builder.Password,
            UseWindowsAuth = false,
            // ConnectionStringBuilder maps TrustServerCertificate -> SslMode.Required
            // (vs VerifyCA); reverse it so the setting round-trips.
            TrustServerCertificate = builder.SslMode == MySqlConnector.MySqlSslMode.Required
        };
    }

    private static DatabaseConfiguration BuildSqliteConfiguration(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        return new DatabaseConfiguration
        {
            DatabaseType = DatabaseType.SQLite,
            FilePath = builder.DataSource,
            Database = Path.GetFileNameWithoutExtension(builder.DataSource)
        };
    }
}
