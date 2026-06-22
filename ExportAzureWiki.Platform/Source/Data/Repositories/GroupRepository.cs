using System.Data;
using Dapper;
using ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository implementation for IdentityGroup entities
/// </summary>
public class GroupRepository : BaseRepository<IdentityGroup>, IGroupRepository
{
    public GroupRepository(IDbConnection connection, DatabaseType databaseType)
        : base(connection, databaseType, databaseType == DatabaseType.SqlServer ? "IdentityGroups" : "identity_groups")
    {
    }

    public async Task<IdentityGroup?> GetByNameAsync(string name)
    {
        var columnName = DatabaseType == DatabaseType.SqlServer ? "Name" : "name";
        var sql = $"SELECT * FROM {GetQualifiedTableName()} WHERE {columnName} = @Name";
        return await Connection.QuerySingleOrDefaultAsync<IdentityGroup>(sql, new { Name = name }).ConfigureAwait(false);
    }

    public async Task<IEnumerable<IdentityGroup>> GetByUserIdAsync(int userId)
    {
        var groupTable = GetQualifiedTableName();
        var userGroupTable = DatabaseType == DatabaseType.SqlServer ? "[dbo].[UserIdentityGroups]" : "user_identity_groups";

        var groupIdCol = DatabaseType == DatabaseType.SqlServer ? "g.Id" : "g.id";
        var userIdCol = DatabaseType == DatabaseType.SqlServer ? "ug.UserId" : "ug.user_id";
        var groupIdJoin = DatabaseType == DatabaseType.SqlServer ? "ug.GroupId" : "ug.group_id";

        var sql = $@"SELECT g.* FROM {groupTable} g
                     INNER JOIN {userGroupTable} ug ON {groupIdCol} = {groupIdJoin}
                     WHERE {userIdCol} = @UserId";

        return await Connection.QueryAsync<IdentityGroup>(sql, new { UserId = userId }).ConfigureAwait(false);
    }

    public async Task<IEnumerable<User>> GetUsersByGroupIdAsync(int groupId)
    {
        var userTable = DatabaseType == DatabaseType.SqlServer ? "[dbo].[Users]" : "users";
        var userGroupTable = DatabaseType == DatabaseType.SqlServer ? "[dbo].[UserIdentityGroups]" : "user_identity_groups";

        var userIdCol = DatabaseType == DatabaseType.SqlServer ? "u.Id" : "u.id";
        var groupIdCol = DatabaseType == DatabaseType.SqlServer ? "ug.GroupId" : "ug.group_id";
        var userIdJoin = DatabaseType == DatabaseType.SqlServer ? "ug.UserId" : "ug.user_id";

        var sql = $@"SELECT u.* FROM {userTable} u
                     INNER JOIN {userGroupTable} ug ON {userIdCol} = {userIdJoin}
                     WHERE {groupIdCol} = @GroupId";

        return await Connection.QueryAsync<User>(sql, new { GroupId = groupId }).ConfigureAwait(false);
    }

    public async Task<bool> AddUserAsync(int userId, int groupId)
    {
        var tableName = DatabaseType == DatabaseType.SqlServer ? "[dbo].[UserIdentityGroups]" : "user_identity_groups";
        var userIdCol = DatabaseType == DatabaseType.SqlServer ? "UserId" : "user_id";
        var groupIdCol = DatabaseType == DatabaseType.SqlServer ? "GroupId" : "group_id";

        var sql = $"INSERT INTO {tableName} ({userIdCol}, {groupIdCol}) VALUES (@UserId, @GroupId)";

        try
        {
            await Connection.ExecuteAsync(sql, new { UserId = userId, GroupId = groupId }).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false; // Duplicate key or other constraint violation
        }
    }

    public async Task<bool> RemoveUserAsync(int userId, int groupId)
    {
        var tableName = DatabaseType == DatabaseType.SqlServer ? "[dbo].[UserIdentityGroups]" : "user_identity_groups";
        var userIdCol = DatabaseType == DatabaseType.SqlServer ? "UserId" : "user_id";
        var groupIdCol = DatabaseType == DatabaseType.SqlServer ? "GroupId" : "group_id";

        var sql = $"DELETE FROM {tableName} WHERE {userIdCol} = @UserId AND {groupIdCol} = @GroupId";
        var rowsAffected = await Connection.ExecuteAsync(sql, new { UserId = userId, GroupId = groupId }).ConfigureAwait(false);
        return rowsAffected > 0;
    }
}
