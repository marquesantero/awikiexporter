using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Services;
using Serilog;

namespace ExportAzureWiki.Platform.Services;

public sealed class WikiPageRenderService : IWikiPageRenderService
{
    private readonly IWikiServiceFactory _wikiServiceFactory;

    public WikiPageRenderService() : this(new WikiServiceFactory())
    {
    }

    public WikiPageRenderService(IWikiServiceFactory wikiServiceFactory)
    {
        _wikiServiceFactory = wikiServiceFactory ?? throw new ArgumentNullException(nameof(wikiServiceFactory));
    }

    public async Task<IReadOnlyList<RenderedWikiPage>> RenderWikiPagesAsync(
        WikiConfiguration configuration,
        IReadOnlyList<string> pagePaths,
        bool forceRefreshCache,
        bool offlineMode)
    {
        if (configuration == null || pagePaths.Count == 0)
        {
            return [];
        }

        return await RenderViaWikiServiceAsync(configuration, pagePaths, forceRefreshCache, offlineMode).ConfigureAwait(false);
    }

    /// <summary>
    /// Source-agnostic render: pull each page's Markdown from the platform's
    /// IWikiService and run it through the shared Markdig + sanitizer pipeline.
    /// A failure on one page is logged and skipped, never aborting the batch.
    /// </summary>
    private async Task<IReadOnlyList<RenderedWikiPage>> RenderViaWikiServiceAsync(
        WikiConfiguration configuration,
        IReadOnlyList<string> pagePaths,
        bool forceRefreshCache,
        bool offlineMode)
    {
        var service = _wikiServiceFactory.CreateService(configuration);
        var generator = CreateHtmlContentGenerator(configuration);

        // Local source-Markdown cache. Re-clicking "load" reuses these files
        // without re-fetching from the network; only a forced refresh (or a
        // page never fetched) goes to the provider. Offline mode renders
        // whatever is already cached and skips the rest.
        var sourceCacheDir = WikiCachePaths.Source(WikiCacheScopeHelper.FromWikiId(configuration.Id));
        Directory.CreateDirectory(sourceCacheDir);

        var results = new List<RenderedWikiPage>();
        foreach (var path in pagePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var srcFile = Path.Combine(sourceCacheDir, SanitizeFileName(path) + ".md");
                var haveCache = File.Exists(srcFile);
                var shouldFetch = !offlineMode && (forceRefreshCache || !haveCache);

                string? content = null;
                if (shouldFetch)
                {
                    var fetched = await service.GetPageContentAsync(path).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(fetched?.Content))
                    {
                        content = fetched.Content;
                        // Encrypted at rest: the offline source cache holds the
                        // verbatim wiki content, so it must not sit in plaintext.
                        await SecureCacheFile.WriteTextAsync(srcFile, content).ConfigureAwait(false);
                    }
                }

                if (content == null && haveCache)
                {
                    // Reused from cache (decrypted in memory): no network round-trip.
                    content = await SecureCacheFile.ReadTextAsync(srcFile).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    Log.Warning("Wiki page {Path} has no content (offline={Offline}) from {Platform}",
                        path, offlineMode, configuration.Platform);
                    continue;
                }

                // Stable fingerprint: the content's own SHA-256 (added inside the
                // generator) drives cache invalidation, so an unchanged page is a
                // cache hit and is not re-rendered. No volatile timestamps here.
                var fingerprint = $"{configuration.Platform}|{configuration.Id}|{path}";
                var rendered = await generator.GenerateContentFromMarkdownAsync(
                    path,
                    content,
                    sourceFingerprint: fingerprint,
                    forceRefreshCache: forceRefreshCache,
                    dialect: ResolveMarkdownDialect(configuration.Platform)).ConfigureAwait(false);
                if (rendered != null && !string.IsNullOrWhiteSpace(rendered.HtmlFilePath))
                {
                    results.Add(new RenderedWikiPage { Path = path, HtmlFilePath = rendered.HtmlFilePath });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to render wiki page {Path} from {Platform}", path, configuration.Platform);
            }
        }

        return results;
    }

    /// <summary>Maps a wiki page path to a safe flat file name for the source cache.</summary>
    internal static string SanitizeFileName(string path)
    {
        var name = path.Replace('\\', '/').Trim('/').Replace('/', '_');
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^3];
        }
        return string.IsNullOrWhiteSpace(name) ? "page" : name;
    }

    public async Task<RenderedWikiPage?> RenderLocalMarkdownAsync(string markdownFilePath)
    {
        if (string.IsNullOrWhiteSpace(markdownFilePath) || !File.Exists(markdownFilePath))
        {
            return null;
        }

        var generator = CreateHtmlContentGenerator(new WikiConfiguration());
        // Markdig + sanitizer + file hashing are CPU-bound with no UI
        // affinity; keep them off the WPF dispatcher.
        var rendered = await Task.Run(() => generator.GenerateContentFromLocalMarkdownAsync(markdownFilePath)).ConfigureAwait(false);
        if (rendered == null || string.IsNullOrWhiteSpace(rendered.HtmlFilePath))
        {
            return null;
        }

        return new RenderedWikiPage
        {
            Path = rendered.Page?.Path ?? markdownFilePath,
            HtmlFilePath = rendered.HtmlFilePath
        };
    }

    private static HtmlContentGenerator CreateHtmlContentGenerator(WikiConfiguration configuration)
    {
        var themeManager = new ThemeManager();
        themeManager.LoadThemes();
        var cacheScope = WikiCacheScopeHelper.FromWikiId(configuration.Id);

        var azureService = new AzureDevOpsService();

        return new HtmlContentGenerator(azureService, themeManager, cacheScope);
    }

    private static WikiMarkdownDialect ResolveMarkdownDialect(WikiPlatform platform)
        => platform switch
        {
            WikiPlatform.AzureDevOps => WikiMarkdownDialect.AzureDevOps,
            WikiPlatform.GitHub => WikiMarkdownDialect.GitHub,
            WikiPlatform.GitLab => WikiMarkdownDialect.GitLab,
            WikiPlatform.Bitbucket => WikiMarkdownDialect.Bitbucket,
            _ => WikiMarkdownDialect.Generic
        };
}
