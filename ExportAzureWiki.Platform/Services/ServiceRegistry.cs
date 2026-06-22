using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Platform.Services;

/// <summary>
/// Default <see cref="IAppServiceSet"/>. Composed by the DI container
/// at startup (see <see cref="PlatformServiceCollectionExtensions"/>);
/// the previous <c>CreateDefault()</c> static helper is replaced by
/// the <c>AddExportAzureWikiPlatform</c> extension method on
/// <c>IServiceCollection</c>.
/// </summary>
public sealed class ServiceRegistry : IAppServiceSet
{
    public IAuthenticationService Authentication { get; }
    public IWikiCatalogService WikiCatalog { get; }
    public IWikiPageBrowserService WikiPageBrowser { get; }
    public IWikiPageRenderService WikiPageRenderer { get; }
    public IAdminCatalogService AdminCatalog { get; }
    public IAiTextGenerationService AiTextGeneration { get; }
    public IAiProviderProbeService AiProviderProbe { get; }
    public IDocumentExportService DocumentExport { get; }
    public IExportHistoryService ExportHistory { get; }

    public ServiceRegistry(
        IAuthenticationService authentication,
        IWikiCatalogService wikiCatalog,
        IWikiPageBrowserService wikiPageBrowser,
        IWikiPageRenderService wikiPageRenderer,
        IAdminCatalogService adminCatalog,
        IAiTextGenerationService aiTextGeneration,
        IAiProviderProbeService aiProviderProbe,
        IDocumentExportService documentExport,
        IExportHistoryService exportHistory)
    {
        Authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        WikiCatalog = wikiCatalog ?? throw new ArgumentNullException(nameof(wikiCatalog));
        WikiPageBrowser = wikiPageBrowser ?? throw new ArgumentNullException(nameof(wikiPageBrowser));
        WikiPageRenderer = wikiPageRenderer ?? throw new ArgumentNullException(nameof(wikiPageRenderer));
        AdminCatalog = adminCatalog ?? throw new ArgumentNullException(nameof(adminCatalog));
        AiTextGeneration = aiTextGeneration ?? throw new ArgumentNullException(nameof(aiTextGeneration));
        AiProviderProbe = aiProviderProbe ?? throw new ArgumentNullException(nameof(aiProviderProbe));
        DocumentExport = documentExport ?? throw new ArgumentNullException(nameof(documentExport));
        ExportHistory = exportHistory ?? throw new ArgumentNullException(nameof(exportHistory));
    }
}
