namespace ExportAzureWiki.Data;

/// <summary>
/// Supported database types for the application
/// </summary>
public enum DatabaseType
{
    /// <summary>
    /// Microsoft SQL Server (Windows Authentication or SQL Authentication)
    /// </summary>
    SqlServer,

    /// <summary>
    /// PostgreSQL database
    /// </summary>
    PostgreSQL,

    /// <summary>
    /// MySQL or MariaDB database
    /// </summary>
    MySQL,

    /// <summary>
    /// SQLite embedded database (file-based)
    /// </summary>
    SQLite
}
