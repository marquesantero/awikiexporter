namespace ExportAzureWiki.Models
{
    public enum WikiPlatform
    {
        // Markdown + Git sources only (see ExportAzureWiki.Core.Models.WikiPlatform).
        AzureDevOps,
        GitHub,
        GitLab,
        Bitbucket
    }

    public enum AuthenticationType
    {
        PersonalAccessToken,
        OAuth,
        BasicAuth,
        ApiKey,
        None
    }

    public enum WikiVisibilityScope
    {
        Global,
        Personal
    }

    public class WikiConfiguration
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public WikiPlatform Platform { get; set; } = WikiPlatform.AzureDevOps;
        public string BaseUrl { get; set; } = string.Empty;
        public AuthenticationType AuthType { get; set; } = AuthenticationType.PersonalAccessToken;
        public Dictionary<string, string> AuthenticationData { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> PlatformSpecificData { get; set; } = new Dictionary<string, string>();
        public string RootPath { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUsedAt { get; set; } = DateTime.Now;
        public string IconColor { get; set; } = "#0078D4";
        public bool IsActive { get; set; } = true;
        public WikiVisibilityScope VisibilityScope { get; set; } = WikiVisibilityScope.Global;
        public string OwnerUserId { get; set; } = string.Empty;
        public string OwnerDisplayName { get; set; } = string.Empty;
        public bool CreatedByAdmin { get; set; }

        // Helper properties for backward compatibility with Azure DevOps
        public string OrganizationUrl 
        { 
            get => BaseUrl;
            set => BaseUrl = value;
        }
        
        public string PersonalAccessToken 
        { 
            get => AuthenticationData.ContainsKey("Token") ? AuthenticationData["Token"] : string.Empty;
            set 
            {
                if (!string.IsNullOrEmpty(value))
                    AuthenticationData["Token"] = value;
            }
        }
        
        public string ProjectName 
        { 
            get => PlatformSpecificData.ContainsKey("ProjectName") ? PlatformSpecificData["ProjectName"] : string.Empty;
            set 
            {
                if (!string.IsNullOrEmpty(value))
                    PlatformSpecificData["ProjectName"] = value;
            }
        }
        
        public string ProjectId
        { 
            get => PlatformSpecificData.ContainsKey("ProjectId") ? PlatformSpecificData["ProjectId"] : string.Empty;
            set 
            {
                if (!string.IsNullOrEmpty(value))
                    PlatformSpecificData["ProjectId"] = value;
            }
        }
        
        public string WikiName
        { 
            get => PlatformSpecificData.ContainsKey("WikiName") ? PlatformSpecificData["WikiName"] : string.Empty;
            set 
            {
                if (!string.IsNullOrEmpty(value))
                    PlatformSpecificData["WikiName"] = value;
            }
        }
        
        public string RepositoryId
        { 
            get => PlatformSpecificData.ContainsKey("RepositoryId") ? PlatformSpecificData["RepositoryId"] : string.Empty;
            set 
            {
                if (!string.IsNullOrEmpty(value))
                    PlatformSpecificData["RepositoryId"] = value;
            }
        }
    }

    public class WikiConfigurationList
    {
        public List<WikiConfiguration> Configurations { get; set; } = new List<WikiConfiguration>();
        public string DefaultConfigurationId { get; set; } = string.Empty;
        public int Version { get; set; } = 2;
    }

    public static class WikiPlatformSettings
    {
        public static Dictionary<WikiPlatform, PlatformInfo> Platforms = new Dictionary<WikiPlatform, PlatformInfo>
        {
            [WikiPlatform.AzureDevOps] = new PlatformInfo 
            { 
                Name = "Azure DevOps",
                Icon = "☁",
                Color = "#0078D4",
                RequiredFields = new[] { "OrganizationUrl", "PersonalAccessToken", "ProjectName", "WikiName" },
                UrlPattern = "https://dev.azure.com/{organization}"
            },
            [WikiPlatform.GitHub] = new PlatformInfo 
            { 
                Name = "GitHub",
                Icon = "🐙",
                Color = "#24292e",
                RequiredFields = new[] { "Owner", "Repository", "Token" },
                UrlPattern = "https://github.com/{owner}/{repo}/wiki"
            },
            [WikiPlatform.GitLab] = new PlatformInfo 
            { 
                Name = "GitLab",
                Icon = "🦊",
                Color = "#FC6D26",
                RequiredFields = new[] { "ProjectId", "Token" },
                UrlPattern = "https://gitlab.com/{namespace}/{project}/-/wikis"
            },
            [WikiPlatform.Bitbucket] = new PlatformInfo
            {
                Name = "Bitbucket",
                Icon = "🔷",
                Color = "#0052CC",
                RequiredFields = new[] { "Workspace", "Repository", "AppPassword" },
                UrlPattern = "https://bitbucket.org/{workspace}/{repo}/wiki"
            }
        };
    }

    public class PlatformInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string[] RequiredFields { get; set; } = Array.Empty<string>();
        public string UrlPattern { get; set; } = string.Empty;
    }
}
