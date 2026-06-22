using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Data;
using ExportAzureWiki.Platform.Services;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Platform.Backend;

internal sealed class WikiBackend : IWikiBackend
{
    private readonly WikiConfigurationStorageService _storage;
    private readonly IWikiServiceFactory _wikiServiceFactory;

    public WikiBackend() : this(new WikiServiceFactory())
    {
    }

    internal WikiBackend(IWikiServiceFactory wikiServiceFactory)
        : this(new DbConnectionFactory(), wikiServiceFactory)
    {
    }

    internal WikiBackend(IDbConnectionFactory dbConnectionFactory, IWikiServiceFactory wikiServiceFactory)
    {
        _storage = new WikiConfigurationStorageService(dbConnectionFactory);
        _wikiServiceFactory = wikiServiceFactory ?? throw new ArgumentNullException(nameof(wikiServiceFactory));
    }

    public Task<IReadOnlyList<WikiConfiguration>> LoadWikiConfigurationsAsync()
    {
        return Task.Run<IReadOnlyList<WikiConfiguration>>(() =>
            _storage.LoadAll()
                .Select(ProviderModelMapper.ToCore)
                .ToList());
    }

    public Task SaveWikiConfigurationsAsync(IReadOnlyList<WikiConfiguration> items)
    {
        return Task.Run(() =>
            _storage.SaveAll(items.Select(ProviderModelMapper.ToProvider).ToList()));
    }

    public Task<bool> DeleteWikiConfigurationByIdAsync(string id)
    {
        return Task.Run(() => _storage.DeleteById(id));
    }

    public async Task<IReadOnlyList<WikiPage>> GetPagesAsync(WikiConfiguration configuration)
    {
        var service = _wikiServiceFactory.CreateService(configuration);
        return await service.GetPagesAsync().ConfigureAwait(false);
    }

    public async Task<WikiPageContent?> GetPageContentAsync(WikiConfiguration configuration, string pagePath)
    {
        var service = _wikiServiceFactory.CreateService(configuration);
        return await service.GetPageContentAsync(pagePath).ConfigureAwait(false);
    }
}









