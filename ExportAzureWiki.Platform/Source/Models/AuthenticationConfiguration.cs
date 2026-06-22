namespace ExportAzureWiki.Models;

/// <summary>
/// Configuration for authentication methods
/// </summary>
public class AuthenticationConfiguration
{
    public int Id { get; set; }

    /// <summary>
    /// Primary authentication method
    /// </summary>
    public AuthenticationMethod PrimaryMethod { get; set; } = AuthenticationMethod.Local;

    /// <summary>
    /// Allow Windows Authentication
    /// </summary>
    public bool AllowWindowsAuth { get; set; } = false;

    /// <summary>
    /// Allow Azure AD Authentication
    /// </summary>
    public bool AllowAzureAD { get; set; } = false;

    /// <summary>
    /// Allow local authentication (username/password)
    /// </summary>
    public bool AllowLocalAuth { get; set; } = true;

    /// <summary>
    /// Require authentication to use the application
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// Sync groups from Azure AD
    /// </summary>
    public bool SyncAzureADGroups { get; set; } = false;

    /// <summary>
    /// Sync groups from Windows/Active Directory
    /// </summary>
    public bool SyncWindowsGroups { get; set; } = false;

    /// <summary>
    /// Azure AD Tenant ID for group sync
    /// </summary>
    public string? AzureADTenantId { get; set; }

    /// <summary>
    /// Auto-create users on first login
    /// </summary>
    public bool AutoCreateUsers { get; set; } = false;

    /// <summary>
    /// Default role for auto-created users
    /// </summary>
    public string DefaultRole { get; set; } = "User";

    /// <summary>
    /// Use local permissions (database-based)
    /// </summary>
    public bool UseLocalPermissions { get; set; } = true;

    /// <summary>
    /// Use Azure AD group-based permissions
    /// </summary>
    public bool UseAzureADPermissions { get; set; } = false;

    /// <summary>
    /// Use Windows group-based permissions
    /// </summary>
    public bool UseWindowsPermissions { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Authentication methods
/// </summary>
public enum AuthenticationMethod
{
    /// <summary>
    /// Local database authentication (username/password)
    /// </summary>
    Local = 0,

    /// <summary>
    /// Windows/Active Directory authentication
    /// </summary>
    Windows = 1,

    /// <summary>
    /// Azure Active Directory authentication
    /// </summary>
    AzureAD = 2,

    /// <summary>
    /// OAuth providers (GitHub, Google, etc.)
    /// </summary>
    OAuth = 3,

    /// <summary>
    /// Multiple methods allowed
    /// </summary>
    Multiple = 4
}
