using ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository interface for User entities
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// Gets a user by username
    /// </summary>
    /// <param name="username">Username</param>
    /// <returns>User if found, null otherwise</returns>
    Task<User?> GetByUsernameAsync(string username);

    /// <summary>
    /// Gets a user by email
    /// </summary>
    /// <param name="email">Email address</param>
    /// <returns>User if found, null otherwise</returns>
    Task<User?> GetByEmailAsync(string email);

    /// <summary>
    /// Gets a user by external ID (Azure AD object ID, Windows SID, etc.)
    /// </summary>
    /// <param name="externalId">External ID</param>
    /// <returns>User if found, null otherwise</returns>
    Task<User?> GetByExternalIdAsync(string externalId);

    /// <summary>
    /// Validates user credentials
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="password">Password</param>
    /// <returns>True if credentials are valid, false otherwise</returns>
    Task<bool> ValidateCredentialsAsync(string username, string password);

    /// <summary>
    /// Gets all admin users
    /// </summary>
    /// <returns>List of admin users</returns>
    Task<IEnumerable<User>> GetAdminUsersAsync();

    /// <summary>
    /// Updates the last login timestamp
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>True if successful</returns>
    Task<bool> UpdateLastLoginAsync(int userId);

    /// <summary>
    /// Updates user fields with explicit SQL to avoid accidental data loss from partial payloads.
    /// Sensitive fields are preserved when omitted.
    /// </summary>
    /// <param name="user">User payload</param>
    /// <returns>True if successful</returns>
    Task<bool> UpdateUserSafeAsync(User user);
}
