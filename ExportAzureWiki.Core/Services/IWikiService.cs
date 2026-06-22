using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Core.Services;

public interface IWikiService
{
    WikiPlatform Platform { get; }
    Task<bool> TestConnectionAsync();
    Task<WikiPage> GetRootPageAsync();
    Task<List<WikiPage>> GetPagesAsync(string? parentPath = null);
    Task<WikiPageContent> GetPageContentAsync(string pagePath);
    Task<List<WikiComment>> GetPageCommentsAsync(string pagePath);
    Task<List<WikiAttachment>> GetPageAttachmentsAsync(string pagePath);
    Task<byte[]> GetAttachmentContentAsync(string attachmentPath);
    void Configure(WikiConfiguration configuration);
}
