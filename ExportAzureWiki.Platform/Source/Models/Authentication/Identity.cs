namespace ExportAzureWiki.Models.Authentication
{
    public class Identity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public IdentityType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? ExternalId { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }

    public class Group : Identity
    {
        public List<string> MemberIds { get; set; } = new List<string>();
        public string? ParentGroupId { get; set; }

        // Azure AD specific
        public string? AzureADGroupId { get; set; }
        public string? TenantId { get; set; }

        // Windows specific
        public string? WindowsSid { get; set; }
        public string? Domain { get; set; }

        // GitHub specific
        public string? GitHubOrganization { get; set; }
        public string? GitHubTeamSlug { get; set; }
    }
}
