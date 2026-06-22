namespace ExportAzureWiki.Data.Schema;

/// <summary>
/// Manages database schema creation and versioning
/// </summary>
public interface ISchemaManager
{
    /// <summary>
    /// Checks if the database exists on the server
    /// </summary>
    /// <returns>True if database exists, false otherwise</returns>
    Task<bool> DatabaseExistsAsync();

    /// <summary>
    /// Creates the database if it doesn't exist
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> CreateDatabaseAsync();

    /// <summary>
    /// Creates the complete database schema
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> CreateSchemaAsync();

    /// <summary>
    /// Checks if the schema exists in the database
    /// </summary>
    /// <returns>True if schema exists, false otherwise</returns>
    Task<bool> SchemaExistsAsync();

    /// <summary>
    /// Gets the current schema version from the database
    /// </summary>
    /// <returns>Schema version number, or 0 if not found</returns>
    Task<int> GetSchemaVersionAsync();

    /// <summary>
    /// Verifies if the schema is valid and up to date
    /// </summary>
    /// <returns>True if schema is valid, false otherwise</returns>
    Task<bool> ValidateSchemaAsync();

    /// <summary>
    /// Applies lightweight compatibility migrations to guarantee required tables exist.
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> EnsureRequiredTablesAsync();

    /// <summary>
    /// Seeds the database with pre-configured OAuth providers
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SeedOAuthProvidersAsync();
}
