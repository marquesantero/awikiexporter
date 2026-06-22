using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Platform.Backend;

internal interface IWikiBackend
{
    Task<IReadOnlyList<WikiConfiguration>> LoadWikiConfigurationsAsync();
    Task SaveWikiConfigurationsAsync(IReadOnlyList<WikiConfiguration> items);
    Task<bool> DeleteWikiConfigurationByIdAsync(string id);
    Task<IReadOnlyList<WikiPage>> GetPagesAsync(WikiConfiguration configuration);
    Task<WikiPageContent?> GetPageContentAsync(WikiConfiguration configuration, string pagePath);
}








