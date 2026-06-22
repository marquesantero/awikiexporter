using System.Data;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Data;

/// <summary>
/// Factory interface for creating database connections
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates a new database connection based on the configured connection string
    /// </summary>
    /// <returns>An open database connection</returns>
    Task<IDbConnection> CreateConnectionAsync();

    /// <summary>
    /// Tests the database connection to verify it's working
    /// </summary>
    /// <returns>True if connection is successful, false otherwise</returns>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Gets the currently configured database type
    /// </summary>
    /// <returns>The database type</returns>
    DatabaseType GetDatabaseType();

    /// <summary>
    /// Gets the current connection string (encrypted sensitive data)
    /// </summary>
    /// <returns>The connection string</returns>
    string GetConnectionString();

    /// <summary>
    /// Sets a new connection string and database type
    /// </summary>
    /// <param name="connectionString">The connection string</param>
    /// <param name="databaseType">The database type</param>
    void SetConnection(string connectionString, DatabaseType databaseType);

    /// <summary>
    /// Sets connection from configuration object and persists bootstrap settings
    /// </summary>
    /// <param name="config">Database configuration</param>
    void SetConnectionFromConfig(DatabaseConfiguration config);

    /// <summary>
    /// Loads the current database configuration from bootstrap settings
    /// </summary>
    /// <returns>Database configuration if exists, null otherwise</returns>
    DatabaseConfiguration? LoadConfiguration();
}
