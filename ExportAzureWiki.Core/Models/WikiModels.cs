namespace ExportAzureWiki.Core.Models;

public enum WikiPlatform
{
    // Markdown + Git sources only. Non-Markdown wikis (Confluence, MediaWiki,
    // DokuWiki) and the generic Custom slot were removed: the product targets
    // docs-as-code teams, and those sources never flowed through the export
    // pipeline anyway.
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

public sealed class WikiConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public WikiPlatform Platform { get; set; } = WikiPlatform.AzureDevOps;
    public string BaseUrl { get; set; } = string.Empty;
    public AuthenticationType AuthType { get; set; } = AuthenticationType.PersonalAccessToken;
    public Dictionary<string, string> AuthenticationData { get; set; } = new();
    public Dictionary<string, string> PlatformSpecificData { get; set; } = new();
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

    public string OrganizationUrl
    {
        get => BaseUrl;
        set => BaseUrl = value;
    }

    public string PersonalAccessToken
    {
        get => AuthenticationData.TryGetValue("Token", out var token) ? token : string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                AuthenticationData["Token"] = value;
            }
        }
    }

    public string ProjectName
    {
        get => PlatformSpecificData.TryGetValue("ProjectName", out var value) ? value : string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                PlatformSpecificData["ProjectName"] = value;
            }
        }
    }

    public string ProjectId
    {
        get => PlatformSpecificData.TryGetValue("ProjectId", out var value) ? value : string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                PlatformSpecificData["ProjectId"] = value;
            }
        }
    }

    public string WikiName
    {
        get => PlatformSpecificData.TryGetValue("WikiName", out var value) ? value : string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                PlatformSpecificData["WikiName"] = value;
            }
        }
    }

    public string RepositoryId
    {
        get => PlatformSpecificData.TryGetValue("RepositoryId", out var value) ? value : string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                PlatformSpecificData["RepositoryId"] = value;
            }
        }
    }
}

public sealed class WikiPage
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ParentPath { get; set; } = string.Empty;
    public int Order { get; set; }
    public DateTime LastModified { get; set; }
    public string Author { get; set; } = string.Empty;
    public bool HasChildren { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public sealed class WikiPageContent
{
    public string PageId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = "markdown";
    public string Version { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public sealed class WikiComment
{
    public string Id { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string AuthorImageUrl { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public List<WikiComment> Replies { get; set; } = new();
}

public sealed class WikiAttachment
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public DateTime UploadedDate { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
}

public sealed class RenderedWikiPage
{
    public string Path { get; set; } = string.Empty;
    public string HtmlFilePath { get; set; } = string.Empty;
}
