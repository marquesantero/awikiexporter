using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Yaml;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services;
// ReSharper disable StringLiteralTypo
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace ExportAzureWiki;

public class HtmlContentGenerator
{
    private const string MarkdownRenderAlgorithmVersion = "markdown-dialect-v4-alerts-yaml-emoji";

    /// <summary>
    /// Shared Markdig pipeline. Beyond the advanced extensions (tables, code,
    /// footnotes, task lists, math, etc.) it adds:
    ///   - YAML front matter: the leading "---" metadata block is parsed and
    ///     not emitted as content (otherwise it renders as a stray rule + text).
    ///   - Alert blocks: GitHub callouts "> [!NOTE]/[!TIP]/[!WARNING]/
    ///     [!IMPORTANT]/[!CAUTION]" become styled "markdown-alert" divs.
    ///   - Emoji & smiley: ":rocket:" style shortcodes become Unicode emoji.
    /// </summary>
    private static readonly MarkdownPipeline SharedMarkdownPipeline = BuildMarkdownPipeline();

    private static MarkdownPipeline BuildMarkdownPipeline()
        => new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseYamlFrontMatter()
            .UseAlertBlocks()
            .UseEmojiAndSmiley()
            .Build();

    /// <summary>
    /// Renders Markdown to an HTML fragment with the shared pipeline (no file
    /// IO, no page template). Exposed for tests of the pipeline behavior
    /// (front matter suppression, alert blocks, emoji).
    /// </summary>
    internal static string RenderMarkdownFragment(string markdown)
        => Markdown.ToHtml(markdown ?? string.Empty, SharedMarkdownPipeline);

    /// <summary>
    /// Like <see cref="RenderMarkdownFragment"/> but first applies the math
    /// delimiter normalization that the real page pipeline runs, so tests can
    /// assert that inline/display math survives to MathJax-ready output.
    /// </summary>
    internal static string RenderMarkdownFragmentWithMathNormalize(string markdown)
        => Markdown.ToHtml(NormalizeMathDelimiters(markdown ?? string.Empty), SharedMarkdownPipeline);

    /// <summary>
    /// GitHub-style callout styling for the alert blocks emitted by Markdig's
    /// alert extension (div.markdown-alert with a
    /// .markdown-alert-title). Kept theme-neutral (translucent background, a
    /// colored left rule per severity) so it reads well in both light and dark
    /// mode, and so the colored rule survives the HTML-to-Word conversion.
    /// </summary>
    private const string AlertBlocksCss = """
        .markdown-alert{padding:8px 16px;margin:12px 0;border-left:4px solid #888;border-radius:6px;background:rgba(127,127,127,0.08);}
        .markdown-alert>:first-child{margin-top:0;}
        .markdown-alert>:last-child{margin-bottom:0;}
        .markdown-alert .markdown-alert-title{font-weight:600;margin:0 0 6px 0;text-transform:uppercase;letter-spacing:.3px;font-size:.9em;}
        .markdown-alert-note{border-left-color:#0969da;}.markdown-alert-note .markdown-alert-title{color:#0969da;}
        .markdown-alert-tip{border-left-color:#1a7f37;}.markdown-alert-tip .markdown-alert-title{color:#1a7f37;}
        .markdown-alert-important{border-left-color:#8250df;}.markdown-alert-important .markdown-alert-title{color:#8250df;}
        .markdown-alert-warning{border-left-color:#9a6700;}.markdown-alert-warning .markdown-alert-title{color:#9a6700;}
        .markdown-alert-caution{border-left-color:#cf222e;}.markdown-alert-caution .markdown-alert-title{color:#cf222e;}
        """;
    private const string LocalAssetsHost = "local.assets";
    private const string MermaidVendorRelativePath = "style/vendor/mermaid/mermaid.min.js";
    private const string MermaidCdnUrl = "https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js";
    private const string MathJaxVendorRelativePath = "style/vendor/mathjax/tex-svg.js";
    private const string MathJaxCdnUrl = "https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-svg.js";

    // Content Security Policy applied to every rendered page served to
    // WebView2. The template embeds inline scripts (highlight.js init,
    // mermaid init, MathJax config) that are generated from string literals
    // in this file -- never from wiki content -- so 'unsafe-inline' is
    // acceptable for now. A follow-up should replace inline scripts with
    // nonces and drop 'unsafe-inline'. Defense-in-depth here means: even if
    // HtmlSanitizer misses an injection, object/plugin embeds, base-uri
    // hijacking, and framing are still blocked.
    //
    // NOTE: img-src includes https: to allow inline external images that
    // wiki authors embed without going through the local cache; tighten if
    // that policy changes.
    private static string BuildContentSecurityPolicy()
    {
        return string.Join("; ", new[]
        {
            "default-src 'none'",
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://local.assets",
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://local.assets",
            "img-src 'self' data: blob: https: https://local.images",
            "font-src 'self' data: https: https://cdn.jsdelivr.net",
            "connect-src 'self' https: https://cdn.jsdelivr.net",
            "object-src 'none'",
            "base-uri 'self'",
            "frame-ancestors 'none'",
            "form-action 'none'"
        });
    }

    // --- Shared page-template fragments (were duplicated verbatim in both the
    //     wiki and the local-markdown render paths) -------------------------
    private const string MermaidInitScript = """
                                if (window.mermaid) {
                                    mermaid.initialize({ startOnLoad: false, securityLevel: 'strict' });
                                    window.addEventListener('DOMContentLoaded', () => {
                                        const nodes = document.querySelectorAll('.mermaid');
                                        if (nodes.length > 0) {
                                            mermaid.run({ nodes });
                                        }
                                    });
                                }
                                window.addEventListener('DOMContentLoaded', () => {
                                    document.body.addEventListener('click', (event) => {
                                        const target = event.target;
                                        if (target && target.tagName === 'IMG' && target.dataset.zoomable === '1' && typeof window.toggleImageSize === 'function') {
                                            window.toggleImageSize(target);
                                        }
                                    });
                                });
                                """;

    private const string MathJaxConfigScript = """
                                  window.MathJax = {
                                      tex: {
                                          inlineMath: [['\\(', '\\)']],
                                          displayMath: [['$$', '$$'], ['\\[', '\\]']],
                                          processEscapes: true
                                      },
                                      options: {
                                          skipHtmlTags: ['script', 'noscript', 'style', 'textarea', 'pre', 'code']
                                      }
                                  };
                                  """;

    private const string OverrideCssDark = """
              html, body, .content-body {
                  background-color: #1e1e1e !important;
                  color: #d4d4d4 !important;
              }
              .content-body h2 {
                  color: #d4d4d4 !important;
              }
              .content-body img {
                  display: inline;
                  margin: 0;
                  vertical-align: middle;
                  max-width: 100%;
                  height: auto;
              }
              .content-body img[src^='https://local.images/'] {
                  display: block;
                  margin: 10px auto;
              }
              .content-body table {
                  border-collapse: collapse;
                  margin: 12px 0;
              }
              .content-body th,
              .content-body td {
                  border: 1px solid #4a4a4a;
                  padding: 8px 10px;
                  text-align: left;
                  vertical-align: top;
                  background-color: transparent;
              }
              .content-body .mermaid {
                  margin: 12px 0;
                  overflow-x: auto;
              }
              .hljs, pre code.hljs {
                  background: #2a2a2a !important;
                  color: #d4d4d4 !important;
              }
              """;

    private const string OverrideCssLight = """
              html, body, .content-body {
                  background-color: #ffffff !important;
                  color: #444444 !important;
              }
              .content-body h2 {
                  color: #444444 !important;
              }
              .content-body img {
                  display: inline;
                  margin: 0;
                  vertical-align: middle;
                  max-width: 100%;
                  height: auto;
              }
              .content-body img[src^='https://local.images/'] {
                  display: block;
                  margin: 10px auto;
              }
              .content-body table {
                  border-collapse: collapse;
                  margin: 12px 0;
              }
              .content-body th,
              .content-body td {
                  border: 1px solid #d0d7de;
                  padding: 8px 10px;
                  text-align: left;
                  vertical-align: top;
                  background-color: transparent;
              }
              .content-body .mermaid {
                  margin: 12px 0;
                  overflow-x: auto;
              }
              .hljs, pre code.hljs {
                  background: #f3f3f3 !important;
                  color: #444444 !important;
              }
              """;

    /// <summary>Loaded CSS/JS/script tags shared by every page in a render run.</summary>
    private sealed record PageTemplateAssets(
        string CssMode,
        string CssTheme,
        string Js,
        string MermaidScriptTag,
        string MathJaxScriptTag,
        string AdditionalScripts);

    private readonly string _cacheBasePath;
    private readonly string _imageFolderPath;
    private readonly string _pagesFolderPath;
    private readonly string _cacheScope;
    private readonly string _localImageBaseUrl;
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly ThemeManager _themeManager;
    private readonly AdditionalCdnScriptService _additionalCdnScriptService;
    public CacheRunStats LastCacheStats { get; private set; } = new();

    public HtmlContentGenerator(AzureDevOpsService azureDevOpsService, ThemeManager themeManager, string? cacheScope = null)
    {
        _azureDevOpsService = azureDevOpsService;
        _themeManager = themeManager;
        _additionalCdnScriptService = new AdditionalCdnScriptService();
        _cacheScope = string.IsNullOrWhiteSpace(cacheScope) ? "global" : cacheScope.Trim();
        _localImageBaseUrl = _cacheScope.Equals("global", StringComparison.OrdinalIgnoreCase)
            ? "https://local.images"
            : $"https://local.images/{_cacheScope}";

        _cacheBasePath = WikiCachePaths.Root;
        _pagesFolderPath = WikiCachePaths.Pages(_cacheScope);
        _imageFolderPath = WikiCachePaths.Images(_cacheScope);

        Directory.CreateDirectory(_cacheBasePath);
        Directory.CreateDirectory(_pagesFolderPath);
        Directory.CreateDirectory(_imageFolderPath);
    }

    public async Task<List<PageInfo>> GenerateContentAsync(
        List<AzureDevOpsService.LocalWikiPage> selectedPages,
        bool forceRefreshCache = false,
        bool offlineMode = false)
    {
        LastCacheStats = new CacheRunStats
        {
            OfflineMode = offlineMode,
            TotalRequested = selectedPages.Count
        };

        var pipeline = SharedMarkdownPipeline;

        var isDarkMode = _themeManager.IsDarkMode();
        var assets = await BuildPageTemplateAssetsAsync(isDarkMode).ConfigureAwait(false);

        // Per-page work runs on the thread pool (Task.Run below): cache
        // hashing, Markdig, the HTML sanitizer, and file writes are CPU/IO
        // heavy and previously serialized onto the WPF dispatcher via the
        // captured SynchronizationContext, freezing the UI during export.
        // Pool execution makes the stats increments concurrent, hence the
        // Interlocked counters folded into LastCacheStats at the end.
        var hits = 0;
        var misses = 0;
        var offlineMisses = 0;
        var regenerated = 0;

        var processTasks = selectedPages.Select(localPage => Task.Run(async () =>
        {
            try
            {
                var pageFilePath = GetPageFilePath(localPage.Path);
                var metadataPath = GetMetadataFilePath(pageFilePath);

                if (!forceRefreshCache && File.Exists(pageFilePath))
                {
                    var expectedHash = TryReadMetadataHash(metadataPath);
                    if (string.IsNullOrWhiteSpace(expectedHash) || IsFileHashEqual(pageFilePath, expectedHash))
                    {
                        Interlocked.Increment(ref hits);
                        Console.WriteLine($@"Carregando página do cache: {localPage.Path}");
                        return new PageInfo
                        {
                            Page = localPage,
                            HtmlFilePath = pageFilePath
                        };
                    }
                }

                Interlocked.Increment(ref misses);

                if (offlineMode)
                {
                    Interlocked.Increment(ref offlineMisses);
                    Console.WriteLine($@"Modo offline: página sem cache ignorada: {localPage.Path}");
                    return null;
                }

                var pageContent = await _azureDevOpsService.GetPageContentAsync(localPage.Path).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    Console.WriteLine($@"Ignorando página sem conteúdo: {localPage.Path}");
                    return null;
                }

                pageContent = NormalizeMermaidBlocks(pageContent);
                pageContent = NormalizeMathDelimiters(pageContent);
                pageContent = NormalizePipeTables(pageContent);
                pageContent = await ProcessAndDownloadAttachments(pageContent, _imageFolderPath).ConfigureAwait(false);
                var pageHtmlContent = Markdown.ToHtml(pageContent, pipeline);
                pageHtmlContent = NormalizeMermaidHtml(pageHtmlContent);

                // Strip active content (script, on*, javascript: urls) from
                // the wiki-sourced HTML before it ever reaches the WebView2
                // surface. Pair this with the CSP applied at the host
                // template below.
                pageHtmlContent = Services.HtmlSanitizer.Sanitize(pageHtmlContent);

                // Mark local wiki images as zoomable. We do NOT inject an
                // inline onclick handler -- a CSP that allows inline JS
                // defeats the point of having a CSP -- and instead attach a
                // delegated listener in the init script below.
                pageHtmlContent = Regex.Replace(
                    pageHtmlContent,
                    @"<img\b([^>]*?\bsrc=[""']https://local\.images/[^""']+[""'][^>]*?)\s*/?>",
                    "<img$1 data-zoomable='1'>",
                    RegexOptions.IgnoreCase);

                var htmlContent = BuildPageDocument(assets, pageHtmlContent, isDarkMode);

                Directory.CreateDirectory(Path.GetDirectoryName(pageFilePath)!);

                // Encrypted at rest; the hash below is over the on-disk
                // (encrypted) bytes, so the integrity/cache check stays valid.
                await SecureCacheFile.WriteTextAsync(pageFilePath, htmlContent).ConfigureAwait(false);
                await WriteMetadataHashAsync(metadataPath, ComputeFileSha256(pageFilePath)).ConfigureAwait(false);
                Interlocked.Increment(ref regenerated);

                return new PageInfo
                {
                    Page = localPage,
                    HtmlFilePath = pageFilePath
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"Erro ao processar página {localPage.Path}: {ex.Message}");
                return null;
            }
        })).ToList();

        var results = await Task.WhenAll(processTasks).ConfigureAwait(false);

        LastCacheStats.Hits = hits;
        LastCacheStats.Misses = misses;
        LastCacheStats.OfflineMisses = offlineMisses;
        LastCacheStats.Regenerated = regenerated;

        return results.Where(p => p != null).ToList()!;
    }

    private string GetMetadataFilePath(string htmlFilePath) => $"{htmlFilePath}.meta.json";

    private static string? TryReadMetadataHash(string metadataPath)
    {
        try
        {
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            var json = File.ReadAllText(metadataPath);
            var meta = JsonSerializer.Deserialize<CacheMetadata>(json);
            return meta?.HtmlSha256;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFileHashEqual(string filePath, string expectedSha256)
    {
        try
        {
            var fileHash = ComputeFileSha256(filePath);
            return string.Equals(fileHash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task WriteMetadataHashAsync(string metadataPath, string hash)
    {
        var meta = new CacheMetadata
        {
            HtmlSha256 = hash,
            GeneratedAtUtc = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json).ConfigureAwait(false);
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        return Convert.ToHexString(bytes);
    }

    private static string ComputeTextSha256(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private sealed class CacheMetadata
    {
        public string HtmlSha256 { get; set; } = string.Empty;
        public string SourceFingerprint { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; }
    }

    private string GetPageFilePath(string? pagePath)
    {
        return BuildPageFilePath(_pagesFolderPath, pagePath);
    }

    private static string BuildPageFilePath(string rootPath, string? pagePath)
    {
        var normalizedPath = (pagePath ?? string.Empty).Trim().Trim('/');
        var pathSegments = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizePathSegment)
            .ToList();

        var pageName = pathSegments.LastOrDefault() ?? "page";
        var directoryPath = rootPath;
        if (pathSegments.Count > 0)
        {
            directoryPath = Path.Combine(rootPath, Path.Combine(pathSegments.ToArray()));
        }

        return Path.Combine(directoryPath, $"{pageName}.html");
    }

    private static string SanitizePathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return "_";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(segment.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "_" : cleaned;
    }

    public async Task<PageInfo?> GenerateContentFromLocalMarkdownAsync(
        string markdownFilePath,
        bool forceRefreshCache = false)
    {
        if (string.IsNullOrWhiteSpace(markdownFilePath) || !File.Exists(markdownFilePath))
        {
            return null;
        }

        var pageContent = await File.ReadAllTextAsync(markdownFilePath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(pageContent))
        {
            return null;
        }

        var sourceFingerprint = $"{markdownFilePath}|{new FileInfo(markdownFilePath).LastWriteTimeUtc.Ticks}";
        return await GenerateContentFromMarkdownAsync(
            $"/Local/{Path.GetFileName(markdownFilePath)}",
            pageContent,
            Path.GetFileNameWithoutExtension(markdownFilePath),
            sourceFingerprint,
            forceRefreshCache).ConfigureAwait(false);
    }

    public async Task<PageInfo?> GenerateContentFromMarkdownAsync(
        string pagePath,
        string markdownContent,
        string? title = null,
        string? sourceFingerprint = null,
        bool forceRefreshCache = false,
        WikiMarkdownDialect dialect = WikiMarkdownDialect.Generic)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            return null;
        }

        var pipeline = SharedMarkdownPipeline;

        var isDarkMode = _themeManager.IsDarkMode();
        var assets = await BuildPageTemplateAssetsAsync(isDarkMode).ConfigureAwait(false);

        var pageContent = NormalizeMarkdownForDialect(markdownContent, dialect);

        var pageHtmlContent = Markdown.ToHtml(pageContent, pipeline);
        pageHtmlContent = NormalizeMermaidHtml(pageHtmlContent);

        // Strip active content from Markdown-sourced HTML. See Fase 1.5.
        pageHtmlContent = Services.HtmlSanitizer.Sanitize(pageHtmlContent);

        // Mark local images zoomable via data-attribute; the delegated
        // listener in mermaidInitScript picks them up. No inline onclick.
        pageHtmlContent = Regex.Replace(
            pageHtmlContent,
            @"<img\b([^>]*?\bsrc=[""']https://local\.images/[^""']+[""'][^>]*?)\s*/?>",
            "<img$1 data-zoomable='1'>",
            RegexOptions.IgnoreCase);

        var pageTitle = string.IsNullOrWhiteSpace(title) ? BuildTitleFromPath(pagePath) : title.Trim();
        var htmlContent = BuildPageDocument(assets, pageHtmlContent, isDarkMode);

        var markdownFolder = Path.Combine(_pagesFolderPath, "Markdown");
        Directory.CreateDirectory(markdownFolder);

        var sanitizedName = Regex.Replace(pageTitle, @"[^\w\- ]", "_");
        var fingerprintSource = $"{pagePath}|{sourceFingerprint}|{dialect}|{MarkdownRenderAlgorithmVersion}|{ComputeTextSha256(markdownContent)}|{ThemeManager.SelectedTheme}|{ThemeManager.DarkModeCheckBox}";
        var fingerprintHash = ComputeTextSha256(fingerprintSource);
        var htmlFilePath = Path.Combine(markdownFolder, $"{sanitizedName}_{fingerprintHash[..12]}.html");
        var metadataPath = GetMetadataFilePath(htmlFilePath);

        var fileExists = File.Exists(htmlFilePath);
        var metadataHash = TryReadMetadataHash(metadataPath) ?? string.Empty;
        var reusedFromCache = !forceRefreshCache
            && fileExists
            && !string.IsNullOrWhiteSpace(metadataHash)
            && IsFileHashEqual(htmlFilePath, metadataHash);

        if (!reusedFromCache)
        {
            await SecureCacheFile.WriteTextAsync(htmlFilePath, htmlContent).ConfigureAwait(false);
            var htmlHash = ComputeFileSha256(htmlFilePath);
            var metadata = new CacheMetadata
            {
                HtmlSha256 = htmlHash,
                SourceFingerprint = sourceFingerprint ?? string.Empty,
                GeneratedAtUtc = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        }

        LastCacheStats = new CacheRunStats
        {
            OfflineMode = false,
            TotalRequested = 1,
            Hits = reusedFromCache ? 1 : 0,
            Misses = reusedFromCache ? 0 : 1,
            Regenerated = reusedFromCache ? 0 : 1
        };

        return new PageInfo
        {
            Page = new AzureDevOpsService.LocalWikiPage
            {
                Path = pagePath,
                Content = pageContent,
                SubPages = []
            },
            HtmlFilePath = htmlFilePath
        };
    }

    private static string BuildTitleFromPath(string pagePath)
    {
        var normalized = string.IsNullOrWhiteSpace(pagePath) ? "page" : pagePath.Replace('\\', '/').Trim('/');
        var name = normalized.Contains('/') ? normalized[(normalized.LastIndexOf('/') + 1)..] : normalized;
        var title = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(title) ? "page" : title;
    }

    private async Task<string> ProcessAndDownloadAttachments(string content, string imageFolderPath)
    {
        // Resolve attachments in both markdown and raw HTML:
        // e.g. ![](/.attachments/file.png), <img src="/.attachments/file.png">, <a href="/.attachments/file.pdf">
        var attachmentPathRegex = new Regex(@"(?<path>/\.attachments/[^""')\s>]+)", RegexOptions.IgnoreCase);
        var resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return await attachmentPathRegex.ReplaceAsync(content, async match =>
        {
            var attachmentPath = match.Groups["path"].Value;

            if (resolvedPaths.TryGetValue(attachmentPath, out var cachedPath))
            {
                return cachedPath;
            }

            try
            {
                var localImagePath = await DownloadAndSaveImage(attachmentPath, imageFolderPath).ConfigureAwait(false);
                if (localImagePath != attachmentPath)
                {
                    var fileName = Path.GetFileName(localImagePath);
                    var newImageUrl = $"{_localImageBaseUrl}/{fileName}";
                    resolvedPaths[attachmentPath] = newImageUrl;
                    return newImageUrl;
                }
                else
                {
                    Console.WriteLine($@"Mantendo o link original para anexo: {attachmentPath}");
                    resolvedPaths[attachmentPath] = attachmentPath;
                    return match.Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"Erro ao processar anexo {attachmentPath}: {ex.Message}");
                resolvedPaths[attachmentPath] = attachmentPath;
                return match.Value;
            }
        }).ConfigureAwait(false);
    }

    private string ResolveClientScriptUrl(string baseDirectory, string vendorRelativePath, string cdnUrl)
    {
        var vendorPath = Path.Combine(baseDirectory, vendorRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(vendorPath))
        {
            var relativeFromStyle = vendorRelativePath.StartsWith("style/", StringComparison.OrdinalIgnoreCase)
                ? vendorRelativePath["style/".Length..]
                : vendorRelativePath;
            return $"https://{LocalAssetsHost}/{relativeFromStyle}";
        }

        return cdnUrl;
    }

    private string BuildAdditionalCdnScriptsHtml()
    {
        var scripts = _additionalCdnScriptService.Load();
        if (scripts.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var url in scripts)
        {
            builder.Append("<script src=\"");
            builder.Append(WebUtility.HtmlEncode(url));
            builder.AppendLine("\"></script>");
        }

        return builder.ToString();
    }

    private static string BuildScriptTagWithFallback(string primaryUrl, string fallbackUrl)
    {
        if (string.IsNullOrWhiteSpace(primaryUrl))
        {
            return $"<script src=\"{fallbackUrl}\"></script>";
        }

        if (string.Equals(primaryUrl, fallbackUrl, StringComparison.OrdinalIgnoreCase))
        {
            return $"<script src=\"{primaryUrl}\"></script>";
        }

        var primaryEncoded = WebUtility.HtmlEncode(primaryUrl);
        var fallbackEncoded = WebUtility.HtmlEncode(fallbackUrl);
        return $"<script src=\"{primaryEncoded}\" onerror=\"this.onerror=null;this.src='{fallbackEncoded}';\"></script>";
    }

    /// <summary>Loads the per-run CSS/JS/script assets (mode + theme + highlight + mermaid/mathjax).</summary>
    private async Task<PageTemplateAssets> BuildPageTemplateAssetsAsync(bool isDarkMode)
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var mode = isDarkMode ? "dark-mode.css" : "light-mode.css";
        var cssModeContent = await File.ReadAllTextAsync(Path.Combine(baseDirectory, "style", mode)).ConfigureAwait(false);
        var cssThemeContent = await File.ReadAllTextAsync(_themeManager.GetCurrentTheme()!).ConfigureAwait(false);
        var jsContent = await File.ReadAllTextAsync(Path.Combine(baseDirectory, "style", "highlight.js")).ConfigureAwait(false);
        var mermaidScriptTag = BuildScriptTagWithFallback(
            ResolveClientScriptUrl(baseDirectory, MermaidVendorRelativePath, MermaidCdnUrl), MermaidCdnUrl);
        var mathJaxScriptTag = BuildScriptTagWithFallback(MathJaxCdnUrl, MathJaxCdnUrl);
        return new PageTemplateAssets(
            cssModeContent, cssThemeContent, jsContent, mermaidScriptTag, mathJaxScriptTag, BuildAdditionalCdnScriptsHtml());
    }

    /// <summary>Wraps a rendered body fragment into the full themed HTML document served to WebView2.</summary>
    private static string BuildPageDocument(PageTemplateAssets assets, string bodyHtml, bool isDarkMode)
    {
        var overrideCss = isDarkMode ? OverrideCssDark : OverrideCssLight;
        var bodyStyle = isDarkMode ? "background:#1e1e1e;color:#d4d4d4;" : "background:#ffffff;color:#444444;";
        return $"""
                <html>
                <head>
                    <meta http-equiv="Content-Security-Policy" content="{BuildContentSecurityPolicy()}">
                    <style>{assets.CssMode}</style>
                    <style>{assets.CssTheme}</style>
                    <style>{overrideCss}</style>
                    <style>{AlertBlocksCss}</style>
                    <script>{assets.Js}</script>
                    {assets.MermaidScriptTag}
                    <script>{MathJaxConfigScript}</script>
                    {assets.MathJaxScriptTag}
                    {assets.AdditionalScripts}
                    <script>hljs.highlightAll();</script>
                    <script>{MermaidInitScript}</script>
                </head>
                <body class='content-body' style='{bodyStyle}'>
                {bodyHtml}
                </body>
                </html>
                """;
    }

    private static string NormalizePipeTables(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length + 16);

        for (var i = 0; i < lines.Length; i++)
        {
            var current = lines[i];
            var trimmed = current.Trim();
            var nextTrimmed = i + 1 < lines.Length ? lines[i + 1].Trim() : string.Empty;

            var looksLikeHeader = trimmed.StartsWith("|") && trimmed.Count(c => c == '|') >= 2;
            var looksLikeSeparator = Regex.IsMatch(
                nextTrimmed,
                @"^\|\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)+\|?\s*$");

            if (looksLikeHeader && looksLikeSeparator)
            {
                var prev = output.Count > 0 ? output[^1] : string.Empty;
                if (!string.IsNullOrWhiteSpace(prev))
                {
                    output.Add(string.Empty);
                }
            }

            output.Add(current);
        }

        return string.Join("\n", output);
    }

    public static string NormalizeMarkdownForDialect(string markdown, WikiMarkdownDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        // The ":::mermaid ... :::" container form is Azure DevOps syntax, but
        // authors paste it into GitHub/local files too. Normalize it to a fenced
        // ```mermaid``` block for every dialect; otherwise the custom-container
        // extension wraps the diagram source in <p> and Mermaid reports a syntax
        // error. The helper only touches ":::mermaid" fences, so it is safe.
        var pageContent = NormalizeMermaidBlocks(markdown);

        pageContent = NormalizeMathDelimiters(pageContent);

        return dialect switch
        {
            WikiMarkdownDialect.GitHub or WikiMarkdownDialect.GitLab or WikiMarkdownDialect.Bitbucket
                => NormalizeGitFlavoredPipeTables(pageContent),
            _ => NormalizePipeTables(pageContent)
        };
    }

    internal static string NormalizeGitFlavoredPipeTables(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length + 16);
        var insideFence = false;
        var fenceMarker = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var current = lines[i];
            var trimmedStart = current.TrimStart();

            if (TryGetFenceMarker(trimmedStart, out var marker))
            {
                if (!insideFence)
                {
                    insideFence = true;
                    fenceMarker = marker;
                }
                else if (string.Equals(marker, fenceMarker, StringComparison.Ordinal))
                {
                    insideFence = false;
                    fenceMarker = string.Empty;
                }

                output.Add(current);
                continue;
            }

            var next = i + 1 < lines.Length ? lines[i + 1] : string.Empty;
            if (!insideFence && IsGitFlavoredTableStart(current, next))
            {
                var prev = output.Count > 0 ? output[^1] : string.Empty;
                if (!string.IsNullOrWhiteSpace(prev))
                {
                    output.Add(string.Empty);
                }
            }

            output.Add(current);
        }

        return string.Join("\n", output);
    }

    private static bool TryGetFenceMarker(string trimmedStart, out string marker)
    {
        marker = string.Empty;
        if (trimmedStart.StartsWith("```", StringComparison.Ordinal))
        {
            marker = "```";
            return true;
        }

        if (trimmedStart.StartsWith("~~~", StringComparison.Ordinal))
        {
            marker = "~~~";
            return true;
        }

        return false;
    }

    private static bool IsGitFlavoredTableStart(string headerLine, string delimiterLine)
    {
        var headerCells = SplitMarkdownTableRow(headerLine);
        if (headerCells.Count < 2)
        {
            return false;
        }

        var delimiterCells = SplitMarkdownTableRow(delimiterLine);
        if (delimiterCells.Count != headerCells.Count)
        {
            return false;
        }

        return delimiterCells.All(IsGitFlavoredDelimiterCell);
    }

    private static bool IsGitFlavoredDelimiterCell(string cell)
        => Regex.IsMatch(cell.Trim(), @"^:?-{3,}:?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static List<string> SplitMarkdownTableRow(string line)
    {
        var value = (line ?? string.Empty).Trim();
        if (value.Length == 0 || !ContainsUnescapedPipe(value))
        {
            return [];
        }

        if (value.StartsWith("|", StringComparison.Ordinal))
        {
            value = value[1..];
        }

        if (value.EndsWith("|", StringComparison.Ordinal) && !EndsWithEscapedPipe(value))
        {
            value = value[..^1];
        }

        var cells = new List<string>();
        var cell = new StringBuilder();
        var escaped = false;

        foreach (var ch in value)
        {
            if (escaped)
            {
                cell.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                cell.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '|')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            cell.Append(ch);
        }

        cells.Add(cell.ToString());
        return cells;
    }

    private static bool ContainsUnescapedPipe(string value)
    {
        var escaped = false;
        foreach (var ch in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '|')
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsWithEscapedPipe(string value)
    {
        if (value.Length < 2 || value[^1] != '|')
        {
            return false;
        }

        var slashCount = 0;
        for (var i = value.Length - 2; i >= 0 && value[i] == '\\'; i--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private static string NormalizeMathDelimiters(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>(lines.Length);
        var insideFence = false;
        var fenceMarker = string.Empty;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (Regex.IsMatch(trimmed, @"^(```|~~~)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                var marker = trimmed.StartsWith("```", StringComparison.Ordinal) ? "```" : "~~~";
                if (!insideFence)
                {
                    insideFence = true;
                    fenceMarker = marker;
                }
                else if (string.Equals(marker, fenceMarker, StringComparison.Ordinal))
                {
                    insideFence = false;
                    fenceMarker = string.Empty;
                }

                output.Add(line);
                continue;
            }

            if (insideFence)
            {
                output.Add(line);
                continue;
            }

            // Multi-line display math written as a standalone "\[" ... "\]" block:
            // map the lone delimiter lines to "$$" so Markdig's math extension
            // treats the span between them as a display block.
            var fullyTrimmed = line.Trim();
            if (fullyTrimmed is @"\[" or @"\]")
            {
                output.Add("$$");
                continue;
            }

            output.Add(ConvertBackslashMathToDollar(line));
        }

        return string.Join("\n", output);
    }

    /// <summary>
    /// Author-written LaTeX math delimiters <c>\( \)</c> and <c>\[ \]</c> do not
    /// survive Markdown: Markdig's backslash-escape turns "\(" into "(" and "\)"
    /// into ")" before MathJax can see them, so inline math leaked through as
    /// literal text (e.g. "(\delta)"). Rewrite them to the <c>$</c>/<c>$$</c>
    /// delimiters that Markdig's Mathematics extension parses directly -- it
    /// emits <c>&lt;span class="math"&gt;\(...\)&lt;/span&gt;</c> for MathJax,
    /// which is the only form that renders. Existing <c>$...$</c> / <c>$$...$$</c>
    /// are left for the same extension. Inline code spans are protected so math
    /// delimiters inside <c>`code`</c> are untouched.
    /// </summary>
    private static string ConvertBackslashMathToDollar(string line)
    {
        if (string.IsNullOrEmpty(line) || !line.Contains('\\', StringComparison.Ordinal))
        {
            return line;
        }

        var codeSpans = new List<string>();
        var protectedLine = Regex.Replace(
            line,
            @"`[^`]*`",
            m =>
            {
                var token = $"__AWIKI_CODE_{codeSpans.Count}__";
                codeSpans.Add(m.Value);
                return token;
            },
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        // Display first (\[ ... \] -> $$ ... $$), then inline (\( ... \) -> $ ... $).
        protectedLine = Regex.Replace(
            protectedLine,
            @"\\\[(.+?)\\\]",
            m => "$$" + m.Groups[1].Value.Trim() + "$$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));

        protectedLine = Regex.Replace(
            protectedLine,
            @"\\\((.+?)\\\)",
            m => "$" + m.Groups[1].Value.Trim() + "$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));

        for (var idx = 0; idx < codeSpans.Count; idx++)
        {
            protectedLine = protectedLine.Replace($"__AWIKI_CODE_{idx}__", codeSpans[idx], StringComparison.Ordinal);
        }

        return protectedLine;
    }

    private static string NormalizeMermaidBlocks(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length + 16);
        var insideMermaidContainer = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!insideMermaidContainer && Regex.IsMatch(trimmed, @"^:::\s*mermaid\s*$", RegexOptions.IgnoreCase))
            {
                insideMermaidContainer = true;
                output.Add("```mermaid");
                continue;
            }

            if (insideMermaidContainer && trimmed == ":::")
            {
                insideMermaidContainer = false;
                output.Add("```");
                continue;
            }

            output.Add(line);
        }

        if (insideMermaidContainer)
        {
            output.Add("```");
        }

        return string.Join("\n", output);
    }

    private static string NormalizeMermaidHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        // Markdown fenced mermaid -> Mermaid container.
        html = Regex.Replace(
            html,
            @"<pre><code class=""[^""]*\blanguage-mermaid\b[^""]*"">([\s\S]*?)</code></pre>",
            m => $"<div class='mermaid'>{WebUtility.HtmlDecode(m.Groups[1].Value).Trim()}</div>",
            RegexOptions.IgnoreCase);

        // Defensive conversion for nested mermaid wrappers.
        html = Regex.Replace(
            html,
            @"<div class=""mermaid"">\s*<pre><code>([\s\S]*?)</code></pre>\s*</div>",
            m => $"<div class='mermaid'>{WebUtility.HtmlDecode(m.Groups[1].Value).Trim()}</div>",
            RegexOptions.IgnoreCase);

        return html;
    }

    private async Task<string> DownloadAndSaveImage(string imagePath, string imageFolderPath)
    {
        var fileName = Path.GetFileName(imagePath);
        var localImagePath = Path.Combine(imageFolderPath, fileName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localImagePath)!);

            var (imageData, contentType) = await _azureDevOpsService.DownloadAttachmentAsync(imagePath).ConfigureAwait(false);

            if (!contentType.StartsWith("image/"))
            {
                Console.WriteLine($@"O arquivo baixado não é uma imagem: {contentType}");
                return imagePath;
            }

            // Encrypted at rest: the WikiImages cache holds full page assets, so
            // a copy lifted off the machine (or read by another Windows user) is
            // useless. Readers decrypt in memory -- the preview WebView2 via the
            // local.images interceptor, and Word/PDF export via a transient
            // decrypted workspace.
            await SecureCacheFile.WriteBytesAsync(localImagePath, imageData).ConfigureAwait(false);

            if (File.Exists(localImagePath))
            {
                Console.WriteLine($@"Imagem salva com sucesso: {localImagePath}");
                return localImagePath;
            }
            else
            {
                Console.WriteLine($@"Falha ao salvar a imagem: {localImagePath}");
                return imagePath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"Erro ao baixar imagem {imagePath}: {ex.Message}");
            return imagePath;
        }
    }
}

// Classe para armazenar informações das páginas para navegação
public class PageInfo
{
    public AzureDevOpsService.LocalWikiPage Page { get; set; }
    public string HtmlFilePath { get; set; }
    public bool IsFavorite { get; set; }
    public List<string> Comments { get; set; } = [];
}
