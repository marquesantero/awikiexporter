using ExportAzureWiki.Models;

namespace ExportAzureWiki.Models.Entities;

/// <summary>
/// User entity for database storage
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public AuthenticationMethod? AuthenticationMethod { get; set; }
    public string? ExternalId { get; set; }
    public string? PreferredLanguage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }

    /// <summary>
    /// Number of consecutive failed login attempts since the last
    /// successful login. Reset to 0 when the user successfully signs in.
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// When set, login attempts before this timestamp are rejected with
    /// a "locked" error. Cleared (NULL) on successful login.
    /// </summary>
    public DateTime? LockedUntil { get; set; }
}
