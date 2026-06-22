using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Platform.Backend;

namespace ExportAzureWiki.Platform.Services;

public sealed class WikiCatalogService : IWikiCatalogService
{
    private readonly IWikiBackend _wikiBackend;

    public WikiCatalogService()
        : this(new WikiBackend())
    {
    }

    internal WikiCatalogService(IWikiBackend wikiBackend)
    {
        _wikiBackend = wikiBackend;
    }

    public Task<IReadOnlyList<WikiConfiguration>> LoadAsync()
    {
        return _wikiBackend.LoadWikiConfigurationsAsync();
    }

    public Task SaveAsync(IReadOnlyList<WikiConfiguration> items)
    {
        return _wikiBackend.SaveWikiConfigurationsAsync(items);
    }

    public Task<bool> DeleteByIdAsync(string id)
    {
        return _wikiBackend.DeleteWikiConfigurationByIdAsync(id);
    }
}







