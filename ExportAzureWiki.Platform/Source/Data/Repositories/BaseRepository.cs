using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using Dapper;
using ExportAzureWiki.Platform.Data;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Base repository implementation with common CRUD operations using Dapper
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public abstract class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly IDbConnection Connection;
    protected readonly DatabaseType DatabaseType;
    protected readonly string TableName;
    private HashSet<string>? _tableColumnsCache;

    protected BaseRepository(IDbConnection connection, DatabaseType databaseType, string tableName)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        DatabaseType = databaseType;
        // All concrete repositories pass a hard-coded literal here. Validate
        // anyway so that if someone wires this up dynamically in the future
        // (a plugin host, a setup wizard, etc.) a hostile table name cannot
        // sneak into the interpolated SQL further down.
        TableName = SqlIdentifier.Validate(tableName);
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        var sql = $"SELECT * FROM {GetQualifiedTableName()} WHERE {GetIdColumnName()} = @Id";
        return await Connection.QuerySingleOrDefaultAsync<T>(sql, new { Id = id }).ConfigureAwait(false);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        var sql = $"SELECT * FROM {GetQualifiedTableName()}";
        return await Connection.QueryAsync<T>(sql).ConfigureAwait(false);
    }

    public virtual async Task<int> AddAsync(T entity)
    {
        var properties = GetProperties(entity);
        var columns = string.Join(", ", properties.Keys);
        var values = string.Join(", ", properties.Keys.Select(k => $"@{k}"));
        var parameters = new DynamicParameters();
        foreach (var kvp in properties)
        {
            parameters.Add(kvp.Key, kvp.Value);
        }

        var sql = $"INSERT INTO {GetQualifiedTableName()} ({columns}) VALUES ({values})";

        if (DatabaseType == DatabaseType.SqlServer)
        {
            sql += "; SELECT CAST(SCOPE_IDENTITY() as int)";
            return await Connection.QuerySingleAsync<int>(sql, parameters).ConfigureAwait(false);
        }
        else if (DatabaseType == DatabaseType.PostgreSQL)
        {
            sql += $" RETURNING {GetIdColumnName()}";
            return await Connection.QuerySingleAsync<int>(sql, parameters).ConfigureAwait(false);
        }
        else if (DatabaseType == DatabaseType.MySQL)
        {
            await Connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
            return await Connection.QuerySingleAsync<int>("SELECT LAST_INSERT_ID()").ConfigureAwait(false);
        }
        else if (DatabaseType == DatabaseType.SQLite)
        {
            await Connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
            return await Connection.QuerySingleAsync<int>("SELECT last_insert_rowid()").ConfigureAwait(false);
        }

        throw new NotSupportedException($"Database type {DatabaseType} is not supported");
    }

    public virtual async Task<bool> UpdateAsync(T entity)
    {
        var properties = GetProperties(entity, includeId: false);
        var setClause = string.Join(", ", properties.Keys.Select(k => $"{k} = @{k}"));
        var idProperty = typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProperty == null)
        {
            throw new InvalidOperationException($"Entity {typeof(T).Name} must have an Id property for update operations.");
        }

        var idValue = idProperty.GetValue(entity);
        var parameters = new DynamicParameters();
        foreach (var kvp in properties)
        {
            parameters.Add(kvp.Key, kvp.Value);
        }
        parameters.Add("__id", idValue);

        var sql = $"UPDATE {GetQualifiedTableName()} SET {setClause} WHERE {GetIdColumnName()} = @__id";
        var rowsAffected = await Connection.ExecuteAsync(sql, parameters).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public virtual async Task<bool> DeleteAsync(int id)
    {
        var sql = $"DELETE FROM {GetQualifiedTableName()} WHERE {GetIdColumnName()} = @Id";
        var rowsAffected = await Connection.ExecuteAsync(sql, new { Id = id }).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    protected string GetQualifiedTableName()
    {
        return DatabaseType switch
        {
            DatabaseType.SqlServer => $"[dbo].[{TableName}]",
            _ => TableName
        };
    }

    protected virtual string GetIdColumnName()
    {
        return DatabaseType switch
        {
            DatabaseType.SqlServer => "Id",
            _ => "id"
        };
    }

    protected Dictionary<string, object?> GetProperties(T entity, bool includeId = false)
    {
        var properties = new Dictionary<string, object?>();
        var type = typeof(T);
        var existingColumns = GetExistingColumns();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip if marked with NotMapped attribute
            if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                continue;

            // Skip Id property if not included
            if (!includeId && prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                continue;

            // Get column name (use property name or Column attribute)
            var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
            var columnName = columnAttr?.Name ?? prop.Name;

            // Convert to database naming convention
            columnName = ConvertToDbNaming(columnName);

            // Ignore model properties that don't exist in the current physical table schema
            if (existingColumns != null && existingColumns.Count > 0 && !existingColumns.Contains(columnName))
                continue;

            properties[columnName] = prop.GetValue(entity);
        }

        return properties;
    }

    protected string ConvertToDbNaming(string propertyName)
    {
        if (DatabaseType == DatabaseType.SqlServer)
        {
            // SQL Server uses PascalCase
            return propertyName;
        }
        else
        {
            // PostgreSQL, MySQL, SQLite use snake_case
            return ToSnakeCase(propertyName);
        }
    }

    private string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new System.Text.StringBuilder();
        result.Append(char.ToLowerInvariant(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
            {
                result.Append('_');
                result.Append(char.ToLowerInvariant(input[i]));
            }
            else
            {
                result.Append(input[i]);
            }
        }

        return result.ToString();
    }

    private HashSet<string>? GetExistingColumns()
    {
        if (_tableColumnsCache != null)
            return _tableColumnsCache;

        try
        {
            IEnumerable<string> columns;

            if (DatabaseType == DatabaseType.SqlServer)
            {
                columns = Connection.Query<string>(
                    @"SELECT COLUMN_NAME
                      FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = 'dbo'
                        AND LOWER(TABLE_NAME) = LOWER(@TableName)",
                    new { TableName = TableName });
            }
            else if (DatabaseType == DatabaseType.PostgreSQL)
            {
                columns = Connection.Query<string>(
                    @"SELECT column_name
                      FROM information_schema.columns
                      WHERE table_schema = 'public'
                        AND LOWER(table_name) = LOWER(@TableName)",
                    new { TableName = TableName });
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                columns = Connection.Query<string>(
                    @"SELECT COLUMN_NAME
                      FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE()
                        AND LOWER(TABLE_NAME) = LOWER(@TableName)",
                    new { TableName = TableName });
            }
            else
            {
                columns = Connection.Query<string>(
                    $"SELECT name FROM pragma_table_info('{TableName}')");
            }

            _tableColumnsCache = columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _tableColumnsCache;
        }
        catch
        {
            // If schema introspection fails, keep previous behavior.
            _tableColumnsCache = null;
            return null;
        }
    }
}
