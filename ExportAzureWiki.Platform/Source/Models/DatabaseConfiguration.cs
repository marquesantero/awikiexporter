using ExportAzureWiki.Data;

namespace ExportAzureWiki.Models;

/// <summary>
/// Configuration model for database connection
/// </summary>
public class DatabaseConfiguration
{
    /// <summary>
    /// The type of database
    /// </summary>
    public DatabaseType DatabaseType { get; set; }

    /// <summary>
    /// Server/Host address
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// Database name
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Username for authentication (if using SQL authentication)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for authentication (if using SQL authentication)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Use Windows Authentication (SQL Server only)
    /// </summary>
    public bool UseWindowsAuth { get; set; }

    /// <summary>
    /// Port number (default ports used if not specified)
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// File path for SQLite database
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// When true, the connection accepts self-signed or untrusted TLS
    /// certificates. Default is false: TLS is required and the server
    /// certificate must validate against the system trust store. Only
    /// enable in lab/test environments after a deliberate decision.
    /// </summary>
    public bool TrustServerCertificate { get; set; }

    /// <summary>
    /// Gets the default port for the database type
    /// </summary>
    public int GetDefaultPort()
    {
        return DatabaseType switch
        {
            DatabaseType.SqlServer => 1433,
            DatabaseType.PostgreSQL => 5432,
            DatabaseType.MySQL => 3306,
            DatabaseType.SQLite => 0, // No port for file-based database
            _ => 0
        };
    }
}
