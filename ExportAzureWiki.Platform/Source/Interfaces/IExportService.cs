using Microsoft.TeamFoundation.Wiki.WebApi;
using static ExportAzureWiki.AzureDevOpsService;

namespace ExportAzureWiki.Interfaces;

public interface IExportService
{
    void ExportToWord(string htmlContent, string filePath, string? wordTemplatePath = null, bool applyWordFineTune = false);
    Task ExportToPdfAsync(string htmlContent, string filePath);
}

public interface IAzureDevOpsService : IDisposable
{
    Task<(byte[] Content, string ContentType)> DownloadAttachmentAsync(string attachmentPath);
    Task<Tuple<Guid, Guid, WikiPageResponse>> GetWikiRootPageAsync();
    Task<string> GetPageContentAsync(string? pagePath);
    Task<List<CommentsWikiPage>> GetPageCommentsAsync(string pagePath);
    void BuildWikiHierarchy(WikiPageResponse parentPage, List<LocalWikiPage> localWikiPages, string parentPath = "");
}

public interface IHtmlContentGenerator
{
    Task<List<PageInfo>> GenerateContentAsync(List<LocalWikiPage> selectedPages);
}

public interface IWikiTreeViewManager
{
    void BuildTreeView(IList<LocalWikiPage> rootPages);
    List<LocalWikiPage> GetCheckedPages();
    void SetRootAndExpandFirstLevel(string rootName);
    void SetRootsAndExpandFirstLevel(IReadOnlyCollection<string> rootPaths);
    void SetDefaultRootAndExpandFirstLevel();
}

public interface IThemeManager
{
    void LoadThemes();
    string? GetCurrentTheme();
    bool IsDarkMode();
}
