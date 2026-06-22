namespace ExportAzureWiki.Core.Models;

public enum AuthenticationMethod
{
    Local = 0,
    Windows = 1,
    AzureAD = 2,
    OAuth = 3,
    Multiple = 4
}

public sealed class UserRecord
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
}

public sealed class ExternalDirectoryUser
{
    public string ExternalId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string ProviderName { get; set; } = string.Empty;
}

public sealed class IdentityGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class OAuthProvider
{
    public int Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string? TenantId { get; set; }
    public string? RedirectUri { get; set; }
    public string? Scopes { get; set; }
    public string? ConfigurationJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}

public sealed class AiProvider
{
    public int Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public string? EndpointUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ModelName { get; set; }
    public string? ApiVersion { get; set; }
    public string? OrganizationId { get; set; }
    public string? ConfigurationJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}

public sealed class AuthenticationConfiguration
{
    public int Id { get; set; }
    public AuthenticationMethod PrimaryMethod { get; set; } = AuthenticationMethod.Local;
    public bool AllowWindowsAuth { get; set; }
    public bool AllowAzureAD { get; set; }
    public bool AllowLocalAuth { get; set; } = true;
    public bool RequireAuthentication { get; set; } = true;
    public bool SyncAzureADGroups { get; set; }
    public bool SyncWindowsGroups { get; set; }
    public string? AzureADTenantId { get; set; }
    public bool AutoCreateUsers { get; set; } = false;
    public string DefaultRole { get; set; } = "User";
    public bool UseLocalPermissions { get; set; } = true;
    public bool UseAzureADPermissions { get; set; }
    public bool UseWindowsPermissions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

public enum AccessPolicyIdentityType
{
    User = 0,
    Group = 1
}

public sealed class SystemAccessPermissions
{
    public bool ManageWikis { get; set; }
    public bool ManageUsersAndGroups { get; set; }
    public bool ManagePermissions { get; set; }
}

public sealed class WikiAccessRule
{
    public string WikiId { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public string StartPoints { get; set; } = string.Empty;
    public bool CanComment { get; set; }
    public bool CanExportWord { get; set; }
    public bool CanExportPdf { get; set; }
    public bool CanUseLetterhead { get; set; }
}

public sealed class AccessPolicy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public AccessPolicyIdentityType IdentityType { get; set; } = AccessPolicyIdentityType.User;
    public string IdentityId { get; set; } = string.Empty;
    public string IdentityDisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public SystemAccessPermissions System { get; set; } = new();
    public List<WikiAccessRule> Wikis { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastModifiedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
}
