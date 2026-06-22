using ExportAzureWiki.Services.WikiProviders;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Platform.Services
{
    public sealed class WikiServiceFactory : IWikiServiceFactory
    {
        public IWikiService CreateService(WikiConfiguration configuration)
        {
            IWikiService service = configuration.Platform switch
            {
                WikiPlatform.AzureDevOps => new AzureDevOpsWikiService(),
                WikiPlatform.GitHub => new GitHubWikiService(),
                WikiPlatform.GitLab => new GitLabWikiService(),
                WikiPlatform.Bitbucket => new BitbucketWikiService(),
                _ => throw new NotSupportedException($"Platform {configuration.Platform} is not supported")
            };

            service.Configure(configuration);
            return service;
        }
    }
}
