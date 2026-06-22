using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Platform.Backend;

namespace ExportAzureWiki.Platform.Services;

public sealed class WikiPageBrowserService : IWikiPageBrowserService
{
    private readonly IWikiBackend _wikiBackend;

    public WikiPageBrowserService()
        : this(new WikiBackend())
    {
    }

    internal WikiPageBrowserService(IWikiBackend wikiBackend)
    {
        _wikiBackend = wikiBackend;
    }

    public Task<IReadOnlyList<ExportAzureWiki.Core.Models.WikiPage>> GetPagesAsync(WikiConfiguration configuration)
    {
        return _wikiBackend.GetPagesAsync(configuration);
    }

    public Task<ExportAzureWiki.Core.Models.WikiPageContent?> GetPageContentAsync(WikiConfiguration configuration, string pagePath)
    {
        return _wikiBackend.GetPageContentAsync(configuration, pagePath);
    }
}







