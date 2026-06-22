using System.Data;
using Dapper;
using ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository implementation for User entities
/// </summary>
public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(IDbConnection connection, DatabaseType databaseType)
        : base(connection, databaseType, databaseType == DatabaseType.SqlServer ? "Users" : "users")
    {
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        var columnName = DatabaseType == DatabaseType.SqlServer ? "Username" : "username";
        var sql = DatabaseType == DatabaseType.SqlServer
            ? $"SELECT * FROM {GetQualifiedTableName()} WHERE {columnName} = @Username"
            : $"SELECT * FROM {GetQualifiedTableName()} WHERE LOWER({columnName}) = LOWER(@Username)";
        return await Connection.QuerySingleOrDefaultAsync<User>(sql, new { Username = username }).ConfigureAwait(false);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        var columnName = DatabaseType == DatabaseType.SqlServer ? "Email" : "email";
        var sql = DatabaseType == DatabaseType.SqlServer
            ? $"SELECT * FROM {GetQualifiedTableName()} WHERE {columnName} = @Email"
            : $"SELECT * FROM {GetQualifiedTableName()} WHERE LOWER({columnName}) = LOWER(@Email)";
        return await Connection.QuerySingleOrDefaultAsync<User>(sql, new { Email = email }).ConfigureAwait(false);
    }

    public async Task<User?> GetByExternalIdAsync(string externalId)
    {
        var columnName = DatabaseType == DatabaseType.SqlServer ? "ExternalId" : "external_id";
        var sql = $"SELECT * FROM {GetQualifiedTableName()} WHERE {columnName} = @ExternalId";
        return await Connection.QuerySingleOrDefaultAsync<User>(sql, new { ExternalId = externalId }).ConfigureAwait(false);
    }

    public async Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        var user = await GetByUsernameAsync(username).ConfigureAwait(false);
        if (user == null || !user.IsActive)
            return false;

        // Password validation will be done by PasswordHashingService
        // This is just a placeholder - the actual validation should be done in the service layer
        return true;
    }

    public async Task<IEnumerable<User>> GetAdminUsersAsync()
    {
        var policiesTable = DatabaseType == DatabaseType.SqlServer ? "[dbo].[AccessPolicies]" : "access_policies";
        var sql = $"""
                   SELECT identity_id
                   FROM {policiesTable}
                   WHERE identity_type = @IdentityType
                     AND is_admin = @IsAdmin
                     AND is_active = @IsActive
                   """;

        var identityIds = await Connection.QueryAsync<string>(sql, new
        {
            IdentityType = 0, // AccessPolicyIdentityType.User
            IsAdmin = true,
            IsActive = true
        }).ConfigureAwait(false);

        var userIds = identityIds
            .Where(v => int.TryParse(v, out _))
            .Select(int.Parse)
            .Distinct()
            .ToArray();

        if (userIds.Length == 0)
        {
            return [];
        }

        var idColumn = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var usersSql = $"SELECT * FROM {GetQualifiedTableName()} WHERE {idColumn} IN @Ids";
        return await Connection.QueryAsync<User>(usersSql, new { Ids = userIds }).ConfigureAwait(false);
    }

    public async Task<bool> UpdateLastLoginAsync(int userId)
    {
        var idColumn = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var loginColumn = DatabaseType == DatabaseType.SqlServer ? "LastLoginAt" : "last_login_at";

        var now = DatabaseType == DatabaseType.SQLite
            ? "datetime('now')"
            : DatabaseType == DatabaseType.SqlServer
                ? "GETDATE()"
                : "CURRENT_TIMESTAMP";

        var sql = $"UPDATE {GetQualifiedTableName()} SET {loginColumn} = {now} WHERE {idColumn} = @UserId";
        var rowsAffected = await Connection.ExecuteAsync(sql, new { UserId = userId }).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateUserSafeAsync(User user)
    {
        var idCol = DatabaseType == DatabaseType.SqlServer ? "Id" : "id";
        var usernameCol = DatabaseType == DatabaseType.SqlServer ? "Username" : "username";
        var emailCol = DatabaseType == DatabaseType.SqlServer ? "Email" : "email";
        var displayNameCol = DatabaseType == DatabaseType.SqlServer ? "DisplayName" : "display_name";
        var passwordHashCol = DatabaseType == DatabaseType.SqlServer ? "PasswordHash" : "password_hash";
        var passwordSaltCol = DatabaseType == DatabaseType.SqlServer ? "PasswordSalt" : "password_salt";
        var isActiveCol = DatabaseType == DatabaseType.SqlServer ? "IsActive" : "is_active";
        var authMethodCol = DatabaseType == DatabaseType.SqlServer ? "AuthenticationMethod" : "authentication_method";
        var externalIdCol = DatabaseType == DatabaseType.SqlServer ? "ExternalId" : "external_id";
        var preferredLanguageCol = DatabaseType == DatabaseType.SqlServer ? "PreferredLanguage" : "preferred_language";
        var createdAtCol = DatabaseType == DatabaseType.SqlServer ? "CreatedAt" : "created_at";
        var lastLoginAtCol = DatabaseType == DatabaseType.SqlServer ? "LastLoginAt" : "last_login_at";
        var lastModifiedAtCol = DatabaseType == DatabaseType.SqlServer ? "LastModifiedAt" : "last_modified_at";

        var now = DatabaseType == DatabaseType.SQLite
            ? "datetime('now')"
            : DatabaseType == DatabaseType.SqlServer
                ? "GETDATE()"
                : "CURRENT_TIMESTAMP";

        var sql = $@"
UPDATE {GetQualifiedTableName()}
SET {usernameCol} = @Username,
    {emailCol} = @Email,
    {displayNameCol} = @DisplayName,
    {passwordHashCol} = CASE WHEN @PasswordHash IS NULL OR @PasswordHash = '' THEN {passwordHashCol} ELSE @PasswordHash END,
    {passwordSaltCol} = CASE WHEN @PasswordSalt IS NULL OR @PasswordSalt = '' THEN {passwordSaltCol} ELSE @PasswordSalt END,
    {isActiveCol} = @IsActive,
    {authMethodCol} = COALESCE(@AuthenticationMethod, {authMethodCol}),
    {externalIdCol} = COALESCE(@ExternalId, {externalIdCol}),
    {preferredLanguageCol} = COALESCE(@PreferredLanguage, {preferredLanguageCol}),
    {createdAtCol} = COALESCE(@CreatedAt, {createdAtCol}),
    {lastLoginAtCol} = COALESCE(@LastLoginAt, {lastLoginAtCol}),
    {lastModifiedAtCol} = {now}
WHERE {idCol} = @Id";

        var rowsAffected = await Connection.ExecuteAsync(sql, new
        {
            user.Id,
            user.Username,
            user.Email,
            user.DisplayName,
            user.PasswordHash,
            user.PasswordSalt,
            user.IsActive,
            user.AuthenticationMethod,
            user.ExternalId,
            user.PreferredLanguage,
            CreatedAt = user.CreatedAt == default ? (DateTime?)null : user.CreatedAt,
            user.LastLoginAt
        }).ConfigureAwait(false);

        return rowsAffected > 0;
    }
}

