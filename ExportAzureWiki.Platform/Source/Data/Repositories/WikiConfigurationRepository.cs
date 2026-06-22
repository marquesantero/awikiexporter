using System.Data;
using Dapper;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository implementation for WikiConfiguration entities
/// </summary>
public class WikiConfigurationRepository : BaseRepository<WikiConfiguration>, IWikiConfigurationRepository
{
    public WikiConfigurationRepository(IDbConnection connection, DatabaseType databaseType)
        : base(connection, databaseType, databaseType == DatabaseType.SqlServer ? "WikiConfigurations" : "wiki_configurations")
    {
    }

    public async Task<WikiConfiguration?> GetDefaultWikiAsync()
    {
        var columnName = DatabaseType == DatabaseType.SqlServer ? "IsDefault" : "is_default";
        var sql = $"SELECT * FROM {GetQualifiedTableName()} WHERE {columnName} = @IsDefault";
        return await Connection.QueryFirstOrDefaultAsync<WikiConfiguration>(sql, new { IsDefault = true }).ConfigureAwait(false);
    }

    public async Task<WikiConfiguration?> GetByIdentifierAsync(string organization, string project, string wikiIdentifier)
    {
        var orgCol = DatabaseType == DatabaseType.SqlServer ? "Organization" : "organization";
        var projCol = DatabaseType == DatabaseType.SqlServer ? "Project" : "project";
        var wikiCol = DatabaseType == DatabaseType.SqlServer ? "WikiIdentifier" : "wiki_identifier";

        var sql = $@"SELECT * FROM {GetQualifiedTableName()}
                     WHERE {orgCol} = @Organization
                     AND {projCol} = @Project
                     AND {wikiCol} = @WikiIdentifier";

        return await Connection.QuerySingleOrDefaultAsync<WikiConfiguration>(sql, new
        {
            Organization = organization,
            Project = project,
            WikiIdentifier = wikiIdentifier
        }).ConfigureAwait(false);
    }

    public async Task<bool> SetDefaultWikiAsync(int wikiId)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var defaultCol = DatabaseType == DatabaseType.SqlServer ? "IsDefault" : "is_default";

        // First, clear all defaults
        var clearSql = $"UPDATE {GetQualifiedTableName()} SET {defaultCol} = @IsDefault";
        await Connection.ExecuteAsync(clearSql, new { IsDefault = false }).ConfigureAwait(false);

        // Then set the specified wiki as default
        var setSql = $"UPDATE {GetQualifiedTableName()} SET {defaultCol} = @IsDefault WHERE {idCol} = @Id";
        var rowsAffected = await Connection.ExecuteAsync(setSql, new { Id = wikiId, IsDefault = true }).ConfigureAwait(false);
        return rowsAffected > 0;
    }
}
