namespace ExportAzureWiki.Models.Authentication;

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

public sealed class EffectiveSystemAccess
{
    public bool IsAdmin { get; set; }
    public bool CanManageWikis { get; set; }
    public bool CanManageUsersAndGroups { get; set; }
    public bool CanManagePermissions { get; set; }
}

public sealed class EffectiveWikiAccess
{
    public bool IsAdmin { get; set; }
    public bool CanView { get; set; }
    public bool CanComment { get; set; }
    public bool CanExportWord { get; set; }
    public bool CanExportPdf { get; set; }
    public bool CanUseLetterhead { get; set; }
    public string StartPoints { get; set; } = string.Empty;
}
