using CoreModels = ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Services.WikiProviders
{
    public abstract class BaseWikiService : IWikiService
    {
        protected CoreModels.WikiConfiguration _configuration = new();

        public abstract CoreModels.WikiPlatform Platform { get; }

        public virtual void Configure(CoreModels.WikiConfiguration configuration)
        {
            _configuration = configuration;
        }

        public virtual Task<bool> TestConnectionAsync()
        {
            return Task.FromResult(false);
        }

        public virtual Task<CoreModels.WikiPage> GetRootPageAsync()
        {
            return Task.FromResult(new CoreModels.WikiPage
            {
                Id = "root",
                Path = "/",
                Title = "Home",
                Order = 0,
                HasChildren = false
            });
        }

        public virtual Task<List<CoreModels.WikiPage>> GetPagesAsync(string? parentPath = null)
        {
            return Task.FromResult(new List<CoreModels.WikiPage>());
        }

        public virtual Task<CoreModels.WikiPageContent> GetPageContentAsync(string pagePath)
        {
            return Task.FromResult(new CoreModels.WikiPageContent
            {
                PageId = pagePath,
                Content = string.Empty,
                ContentType = "markdown"
            });
        }

        public virtual Task<List<CoreModels.WikiComment>> GetPageCommentsAsync(string pagePath)
        {
            return Task.FromResult(new List<CoreModels.WikiComment>());
        }

        public virtual Task<List<CoreModels.WikiAttachment>> GetPageAttachmentsAsync(string pagePath)
        {
            return Task.FromResult(new List<CoreModels.WikiAttachment>());
        }

        public virtual Task<byte[]> GetAttachmentContentAsync(string attachmentPath)
        {
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    // Bitbucket is a Markdown + Git wiki, so it stays in the supported set
    // alongside Azure DevOps, GitHub and GitLab (all three are wired end-to-end
    // through the render/export pipeline). Bitbucket's own connector is still a
    // stub pending implementation.
    public class BitbucketWikiService : BaseWikiService
    {
        public override CoreModels.WikiPlatform Platform => CoreModels.WikiPlatform.Bitbucket;
    }
}
