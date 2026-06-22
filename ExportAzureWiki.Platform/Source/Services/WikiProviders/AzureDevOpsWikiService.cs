using CoreModels = ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Services.WikiProviders
{
    public class AzureDevOpsWikiService : IWikiService
    {
        private CoreModels.WikiConfiguration _configuration = new();
        private AzureDevOpsService? _azureService;

        public CoreModels.WikiPlatform Platform => CoreModels.WikiPlatform.AzureDevOps;

        public void Configure(CoreModels.WikiConfiguration configuration)
        {
            _configuration = configuration;
            _azureService = new AzureDevOpsService(new AppConfig
            {
                OrganizationUrl = configuration.OrganizationUrl,
                PersonalAccessToken = configuration.PersonalAccessToken,
                RepositoryId = configuration.RepositoryId,
                Projectname = configuration.ProjectName,
                WikiName = configuration.WikiName
            });
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (_azureService == null)
            {
                return false;
            }

            try
            {
                var projects = await _azureService.GetProjectsAsync(
                    _configuration.OrganizationUrl,
                    _configuration.PersonalAccessToken).ConfigureAwait(false);
                if (projects == null || projects.Count == 0)
                {
                    return false;
                }

                var project = projects.FirstOrDefault(p =>
                    string.Equals(p.Name, _configuration.ProjectName, StringComparison.OrdinalIgnoreCase));
                if (project == null)
                {
                    return false;
                }

                var wikis = await _azureService.GetWikisAsync(
                    _configuration.OrganizationUrl,
                    _configuration.PersonalAccessToken,
                    project.Id).ConfigureAwait(false);

                return wikis.Any(w => string.Equals(w.Item1, _configuration.WikiName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public async Task<CoreModels.WikiPage> GetRootPageAsync()
        {
            if (_azureService == null)
            {
                return new CoreModels.WikiPage
                {
                    Id = "root",
                    Path = "/",
                    Title = "Home",
                    HasChildren = false
                };
            }

            var (_, _, pageRoot) = await _azureService.GetWikiRootPageAsync().ConfigureAwait(false);
            if (pageRoot?.Page == null)
            {
                return new CoreModels.WikiPage
                {
                    Id = "root",
                    Path = "/",
                    Title = "Home",
                    HasChildren = false
                };
            }

            return ConvertToWikiPage(pageRoot.Page);
        }

        public async Task<List<CoreModels.WikiPage>> GetPagesAsync(string? parentPath = null)
        {
            if (_azureService == null)
            {
                return new List<CoreModels.WikiPage>();
            }

            var (_, _, pageRoot) = await _azureService.GetWikiRootPageAsync().ConfigureAwait(false);
            var localWikiPages = new List<AzureDevOpsService.LocalWikiPage>();
            
            if (pageRoot?.Page != null)
            {
                _azureService.BuildWikiHierarchy(pageRoot.Page, localWikiPages);
            }

            return FlattenLocalPages(localWikiPages).Select(ConvertToWikiPage).ToList();
        }

        public async Task<CoreModels.WikiPageContent> GetPageContentAsync(string pagePath)
        {
            if (_azureService == null)
            {
                return new CoreModels.WikiPageContent
                {
                    PageId = pagePath,
                    Content = string.Empty,
                    ContentType = "markdown",
                    LastModified = DateTime.Now
                };
            }

            // Always fetch real page content from provider API.
            var resolvedPath = pagePath?.Trim() ?? string.Empty;
            var content = string.Empty;
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                try
                {
                    content = await _azureService.GetPageContentAsync(resolvedPath).ConfigureAwait(false);
                }
                catch
                {
                    if (resolvedPath.StartsWith('/'))
                    {
                        content = await _azureService.GetPageContentAsync(resolvedPath.TrimStart('/')).ConfigureAwait(false);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            
            return new CoreModels.WikiPageContent
            {
                PageId = pagePath ?? string.Empty,
                Content = content ?? string.Empty,
                ContentType = "markdown",
                LastModified = DateTime.Now
            };
        }

        public async Task<List<CoreModels.WikiComment>> GetPageCommentsAsync(string pagePath)
        {
            if (_azureService == null)
            {
                return new List<CoreModels.WikiComment>();
            }

            var comments = await _azureService.GetPageCommentsAsync(pagePath).ConfigureAwait(false);
            return comments.Select(c => new CoreModels.WikiComment
            {
                Id = Guid.NewGuid().ToString(),
                Author = c.Author ?? string.Empty,
                AuthorImageUrl = c.ImageAuthor ?? string.Empty,
                Text = c.Text ?? string.Empty,
                CreatedDate = c.CommentDate,
                ModifiedDate = c.CommentDate
            }).ToList();
        }

        public Task<List<CoreModels.WikiAttachment>> GetPageAttachmentsAsync(string pagePath)
        {
            return Task.FromResult(new List<CoreModels.WikiAttachment>());
        }

        public Task<byte[]> GetAttachmentContentAsync(string attachmentPath)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        private static IEnumerable<AzureDevOpsService.LocalWikiPage> FlattenLocalPages(
            IEnumerable<AzureDevOpsService.LocalWikiPage> pages)
        {
            foreach (var page in pages)
            {
                yield return page;

                if (page.SubPages == null || page.SubPages.Count == 0)
                {
                    continue;
                }

                foreach (var child in FlattenLocalPages(page.SubPages))
                {
                    yield return child;
                }
            }
        }

        private CoreModels.WikiPage ConvertToWikiPage(AzureDevOpsService.LocalWikiPage azurePage)
        {
            return new CoreModels.WikiPage
            {
                Id = azurePage.PageId?.ToString() ?? Guid.NewGuid().ToString(),
                Path = azurePage.Path ?? string.Empty,
                Title = azurePage.Path?.Split('/').LastOrDefault() ?? string.Empty,
                Order = 0,
                HasChildren = azurePage.SubPages?.Count > 0
            };
        }

        private CoreModels.WikiPage ConvertToWikiPage(Microsoft.TeamFoundation.Wiki.WebApi.WikiPage azurePage)
        {
            return new CoreModels.WikiPage
            {
                Id = azurePage.Id?.ToString() ?? Guid.NewGuid().ToString(),
                Path = azurePage.Path ?? string.Empty,
                Title = azurePage.Path?.Split('/').LastOrDefault() ?? string.Empty,
                Order = azurePage.Order,
                HasChildren = azurePage.SubPages?.Count > 0
            };
        }
    }
}
