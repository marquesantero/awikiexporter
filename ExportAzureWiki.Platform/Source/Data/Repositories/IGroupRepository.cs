using ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Data.Repositories;

/// <summary>
/// Repository interface for IdentityGroup entities
/// </summary>
public interface IGroupRepository : IRepository<IdentityGroup>
{
    /// <summary>
    /// Gets a group by name
    /// </summary>
    /// <param name="name">Group name</param>
    /// <returns>Group if found, null otherwise</returns>
    Task<IdentityGroup?> GetByNameAsync(string name);

    /// <summary>
    /// Gets all groups for a user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>List of groups</returns>
    Task<IEnumerable<IdentityGroup>> GetByUserIdAsync(int userId);

    /// <summary>
    /// Gets all users in a group
    /// </summary>
    /// <param name="groupId">Group ID</param>
    /// <returns>List of users</returns>
    Task<IEnumerable<User>> GetUsersByGroupIdAsync(int groupId);

    /// <summary>
    /// Adds a user to a group
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="groupId">Group ID</param>
    /// <returns>True if successful</returns>
    Task<bool> AddUserAsync(int userId, int groupId);

    /// <summary>
    /// Removes a user from a group
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="groupId">Group ID</param>
    /// <returns>True if successful</returns>
    Task<bool> RemoveUserAsync(int userId, int groupId);
}
