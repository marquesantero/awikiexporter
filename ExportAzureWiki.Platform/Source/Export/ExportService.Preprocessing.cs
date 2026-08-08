using HtmlAgilityPack;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Services;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace ExportAzureWiki
{
    public partial class ExportService
    {
        internal static string PreprocessHtmlForWord(string htmlContent)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);
            try
            {
                _ = doc.DocumentNode.SelectNodes("//img")?.Count ?? 0;
                _ = doc.DocumentNode.SelectNodes("//a[@href]")?.Count ?? 0;
            }
            catch { }

            // Convert plain image links (<a href="...">) to <img> to ensure Word embeds them.
            var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchors != null)
            {
                foreach (var anchor in anchors.ToList())
                {
                    if (anchor.SelectSingleNode(".//img") != null)
                    {
                        continue;
                    }

                    var href = anchor.GetAttributeValue("href", string.Empty).Trim();
                    if (!LooksLikeImageUrl(href))
                    {
                        continue;
                    }

                    var img = doc.CreateElement("img");
                    img.SetAttributeValue("src", href);
                    var alt = anchor.InnerText?.Trim();
                    if (!string.IsNullOrWhiteSpace(alt))
                    {
                        img.SetAttributeValue("alt", alt);
                    }

                    anchor.ParentNode.ReplaceChild(img, anchor);
                }
            }

            // Remove scripts/styles and other non-content nodes that commonly break conversion.
            var removable = doc.DocumentNode.SelectNodes("//script|//style|//link|//meta|//noscript");
            if (removable != null)
            {
                foreach (var node in removable.ToList())
                {
                    node.Remove();
                }
            }

            // Word-only deterministic Mermaid handling: convert raw mermaid blocks directly to image URL.
            var mermaidDivs = doc.DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' mermaid ')]");
            var mermaidDivCount = mermaidDivs?.Count ?? 0;
            var mermaidConvertedToImg = 0;
            var mermaidAlreadyRendered = 0;
            if (mermaidDivs != null)
            {
                foreach (var mermaidDiv in mermaidDivs.ToList())
                {
                    var alreadyRendered =
                        mermaidDiv.SelectSingleNode(".//img") != null ||
                        mermaidDiv.SelectSingleNode(".//svg") != null ||
                        mermaidDiv.SelectSingleNode(".//canvas") != null;
                    if (alreadyRendered)
                    {
                        if (mermaidDiv.SelectSingleNode(".//img") == null)
                        {
                            var renderedSource = WebUtility.HtmlDecode(
                                mermaidDiv.GetAttributeValue("data-mermaid-source", string.Empty)).Trim();
                            if (!string.IsNullOrWhiteSpace(renderedSource))
                            {
                                var renderedImageUrl = TryBuildMermaidInkUrl(renderedSource);
                                if (!string.IsNullOrWhiteSpace(renderedImageUrl))
                                {
                                    var renderedImg = doc.CreateElement("img");
                                    renderedImg.SetAttributeValue("src", renderedImageUrl);
                                    renderedImg.SetAttributeValue("alt", "Mermaid diagram");
                                    mermaidDiv.ParentNode.ReplaceChild(renderedImg, mermaidDiv);
                                    mermaidConvertedToImg++;
                                    continue;
                                }
                            }
                        }

                        mermaidAlreadyRendered++;
                        continue;
                    }

                    var source = WebUtility.HtmlDecode(mermaidDiv.InnerText ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        continue;
                    }

                    var imageUrl = TryBuildMermaidInkUrl(source);
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        var img = doc.CreateElement("img");
                        img.SetAttributeValue("src", imageUrl);
                        img.SetAttributeValue("alt", "Mermaid diagram");
                        mermaidDiv.ParentNode.ReplaceChild(img, mermaidDiv);
                        mermaidConvertedToImg++;
                        continue;
                    }

                    // Fallback to readable code block if URL generation fails.
                    var code = doc.CreateElement("code");
                    code.SetAttributeValue("class", "language-mermaid");
                    code.InnerHtml = WebUtility.HtmlEncode(source);
                    var pre = doc.CreateElement("pre");
                    pre.AppendChild(code);
                    mermaidDiv.ParentNode.ReplaceChild(pre, mermaidDiv);
                }
            }
            // Mermaid source fallback (Word): convert raw mermaid-like blocks to image URL.
            var preBlocks = doc.DocumentNode.SelectNodes("//pre");
            if (preBlocks != null)
            {
                foreach (var pre in preBlocks.ToList())
                {
                    var codeNode = pre.SelectSingleNode(".//code");
                    var preClass = pre.GetAttributeValue("class", string.Empty);
                    var codeClass = codeNode?.GetAttributeValue("class", string.Empty) ?? string.Empty;
                    var combinedClass = $"{preClass} {codeClass}";
                    var source = WebUtility.HtmlDecode(codeNode?.InnerText ?? pre.InnerText ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        continue;
                    }

                    var isMermaidByClass =
                        combinedClass.Contains("language-mermaid", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(combinedClass, @"\bmermaid\b", RegexOptions.IgnoreCase);
                    var isMermaidByContent = LooksLikeMermaidSource(source);
                    if (!isMermaidByClass && !isMermaidByContent)
                    {
                        continue;
                    }

                    var imageUrl = TryBuildMermaidInkUrl(source);
                    if (string.IsNullOrWhiteSpace(imageUrl))
                    {
                        continue;
                    }

                    var img = doc.CreateElement("img");
                    img.SetAttributeValue("src", imageUrl);
                    img.SetAttributeValue("alt", "Mermaid diagram");
                    pre.ParentNode.ReplaceChild(img, pre);
                }
            }

            // Word pipeline: convert residual Mermaid SVG to image; rasterize any
            // other inline SVG to PNG; only drop it if rasterization fails.
            var svgs = doc.DocumentNode.SelectNodes("//svg");
            if (svgs != null)
            {
                var svgMermaidConverted = 0;
                var svgRasterized = 0;
                var svgRemoved = 0;
                var svgImageDir = ResolveWordImagesCacheFolderPath();
                foreach (var svg in svgs.ToList())
                {
                    var mermaidHost = svg.SelectSingleNode("ancestor::div[contains(concat(' ', normalize-space(@class), ' '), ' mermaid ')]");
                    var mermaidSource = WebUtility.HtmlDecode(
                        mermaidHost?.GetAttributeValue("data-mermaid-source", string.Empty) ?? string.Empty).Trim();

                    if (!string.IsNullOrWhiteSpace(mermaidSource))
                    {
                        var imageUrl = TryBuildMermaidInkUrl(mermaidSource);
                        if (!string.IsNullOrWhiteSpace(imageUrl))
                        {
                            var img = doc.CreateElement("img");
                            img.SetAttributeValue("src", imageUrl);
                            img.SetAttributeValue("alt", "Mermaid diagram");
                            if (mermaidHost != null)
                            {
                                mermaidHost.ParentNode.ReplaceChild(img, mermaidHost);
                            }
                            else
                            {
                                svg.ParentNode.ReplaceChild(img, svg);
                            }

                            svgMermaidConverted++;
                            continue;
                        }
                    }

                    // Non-mermaid inline SVG: rasterize to PNG so it survives the
                    // Word export instead of vanishing.
                    var pngPath = TryRasterizeInlineSvg(svg.OuterHtml, svgImageDir);
                    if (pngPath != null)
                    {
                        var img = doc.CreateElement("img");
                        img.SetAttributeValue("src", pngPath);
                        img.SetAttributeValue("alt", "SVG");
                        svg.ParentNode.ReplaceChild(img, svg);
                        svgRasterized++;
                        continue;
                    }

                    svg.Remove();
                    svgRemoved++;
                }

                LoggingService.LogInfo(
                    $"WORD_SVG: mermaid={svgMermaidConverted}; rasterized={svgRasterized}; dropped={svgRemoved}");
            }

            // GitHub-flavored constructs that the <style> block (stripped above)
            // would normally carry: alert callouts, task-list checkboxes and
            // <details>. Convert them to inline-styled markup HtmlToOpenXml
            // understands, otherwise they render blank or as raw text in Word.
            TransformGitHubFlavoredHtmlForWord(doc);

            // Keep Word tables visually neutral (white) regardless of source theme/css.
            var tableNodes = doc.DocumentNode.SelectNodes("//table|//thead|//tbody|//tr|//th|//td");
            if (tableNodes != null)
            {
                foreach (var n in tableNodes)
                {
                    n.Attributes.Remove("style");
                    n.Attributes.Remove("bgcolor");
                }
            }

            // Insert automatic page break before each top-level section title after the first.
            var sectionHeaders = doc.DocumentNode.SelectNodes("//h1");
            if (sectionHeaders != null && sectionHeaders.Count > 1)
            {
                for (var i = 1; i < sectionHeaders.Count; i++)
                {
                    sectionHeaders[i].SetAttributeValue("data-word-page-break-before", "1");
                }
            }

            // Convert data URI images into temporary local files so Word OpenXml image pipeline can consume them.
            var imageNodes = doc.DocumentNode.SelectNodes("//img[@src]");
            if (imageNodes != null)
            {
                var tempImageDir = ResolveWordImagesCacheFolderPath();

                foreach (var img in imageNodes)
                {
                    var src = img.GetAttributeValue("src", "");
                    if (!src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var localPath = TryPersistDataUriImage(src, tempImageDir);
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        img.SetAttributeValue("src", localPath);
                    }
                }
            }

            try
            {
                _ = doc.DocumentNode.SelectNodes("//img")?.Count ?? 0;
            }
            catch { }

            return doc.DocumentNode.OuterHtml;
        }

        private static readonly Dictionary<string, string> AlertSeverityColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["note"] = "0969da",
            ["tip"] = "1a7f37",
            ["important"] = "8250df",
            ["warning"] = "9a6700",
            ["caution"] = "cf222e"
        };

        /// <summary>
        /// Rewrites GitHub-flavored HTML constructs into markup the
        /// HtmlToOpenXml converter can render: alert callouts become indented
        /// blockquotes with a colored, bold title; task-list checkboxes become
        /// box glyphs; and &lt;details&gt;/&lt;summary&gt; is flattened so the
        /// (otherwise hidden) content is preserved in the document.
        /// </summary>
        internal static void TransformGitHubFlavoredHtmlForWord(HtmlDocument doc)
        {
            StyleMarkdownAlertsForWord(doc);
            ReplaceTaskListCheckboxesForWord(doc);
            FlattenDetailsForWord(doc);
        }

        private static void StyleMarkdownAlertsForWord(HtmlDocument doc)
        {
            var alerts = doc.DocumentNode.SelectNodes(
                "//div[contains(concat(' ', normalize-space(@class), ' '), ' markdown-alert ')]");
            if (alerts == null)
            {
                return;
            }

            foreach (var alert in alerts.ToList())
            {
                var classes = alert.GetAttributeValue("class", string.Empty);
                var severity = AlertSeverityColors.Keys.FirstOrDefault(
                    s => Regex.IsMatch(classes, $@"\bmarkdown-alert-{s}\b", RegexOptions.IgnoreCase));
                var color = severity != null && AlertSeverityColors.TryGetValue(severity, out var c) ? c : "888888";

                var title = alert.SelectSingleNode(
                    ".//*[contains(concat(' ', normalize-space(@class), ' '), ' markdown-alert-title ')]");
                if (title != null)
                {
                    var label = WebUtility.HtmlDecode(title.InnerText ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(label) && severity != null)
                    {
                        label = severity.ToUpperInvariant();
                    }

                    title.Name = "p";
                    title.Attributes.RemoveAll();
                    title.InnerHtml = $"<strong style=\"color:#{color}\">{WebUtility.HtmlEncode(label.ToUpperInvariant())}</strong>";
                }

                // A blockquote gives the callout left indentation in Word; the
                // colored bold title conveys the severity.
                alert.Name = "blockquote";
                alert.Attributes.RemoveAll();
            }
        }

        private static void ReplaceTaskListCheckboxesForWord(HtmlDocument doc)
        {
            var checkboxes = doc.DocumentNode.SelectNodes("//input[@type='checkbox']");
            if (checkboxes == null)
            {
                return;
            }

            foreach (var checkbox in checkboxes.ToList())
            {
                var isChecked = checkbox.Attributes.Contains("checked");
                var glyph = isChecked ? "☑ " : "☐ "; // ☑ / ☐
                var replacement = doc.CreateTextNode(glyph);
                checkbox.ParentNode.ReplaceChild(replacement, checkbox);
            }
        }

        private static void FlattenDetailsForWord(HtmlDocument doc)
        {
            var details = doc.DocumentNode.SelectNodes("//details");
            if (details == null)
            {
                return;
            }

            foreach (var detail in details.ToList())
            {
                var summary = detail.SelectSingleNode(".//summary");
                if (summary != null)
                {
                    var label = WebUtility.HtmlDecode(summary.InnerText ?? string.Empty).Trim();
                    summary.Name = "p";
                    summary.Attributes.RemoveAll();
                    summary.InnerHtml = $"<strong>{WebUtility.HtmlEncode(label)}</strong>";
                }

                // Unwrap <details> so its children render as normal blocks.
                detail.Name = "div";
                detail.Attributes.RemoveAll();
            }
        }

        private static bool LooksLikeMermaidSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var text = source.TrimStart();
            var firstLine = text.Split(['\r', '\n'], 2)[0].Trim();
            var markers = new[]
            {
                "graph ",
                "flowchart ",
                "sequenceDiagram",
                "classDiagram",
                "stateDiagram",
                "erDiagram",
                "gantt",
                "pie ",
                "journey",
                "mindmap",
                "timeline",
                "gitGraph"
            };

            return markers.Any(m => firstLine.StartsWith(m, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeImageUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var candidate = value.Trim().ToLowerInvariant();
            if (candidate.StartsWith("data:image/"))
            {
                return true;
            }

            return candidate.Contains(".png") ||
                   candidate.Contains(".jpg") ||
                   candidate.Contains(".jpeg") ||
                   candidate.Contains(".gif") ||
                   candidate.Contains(".bmp") ||
                   candidate.Contains(".webp") ||
                   candidate.Contains(".svg");
        }

        // Mermaid diagrams are now rendered locally before export (the WPF layer
        // pre-renders them to PNG via an offscreen WebView2). Returning null here
        // disables the old mermaid.ink path so the diagram source never leaves the
        // machine; every caller already falls back to a plain code block when the
        // URL is empty. Kept as a single seam in case a local-render value is
        // wired in later.
        private static string? TryBuildMermaidInkUrl(string source) => null;

        // One shared client for every remote image fetch: avoids a TCP/TLS
        // handshake per badge (a README header has several shields.io badges).
        private static readonly HttpClient _remoteImageClient = new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(8)
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        /// <summary>Deterministic cache key for a remote image URL.</summary>
        private static string RemoteImageCacheKey(string imageUrl)
            => Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)))
                .ToLowerInvariant();

        /// <summary>Returns an already-downloaded copy of this URL, if present.</summary>
        private static string? FindCachedRemoteImage(string outputDirectory, string cacheKey)
        {
            if (!Directory.Exists(outputDirectory))
            {
                return null;
            }
            foreach (var file in Directory.EnumerateFiles(outputDirectory, cacheKey + ".*"))
            {
                return file;
            }
            return null;
        }

        private static string? TryPersistRemoteImage(string imageUrl, string outputDirectory)
        {
            try
            {
                // Cache by URL: re-exporting the same page (or any page sharing a
                // badge) reuses the local copy instead of re-downloading every
                // image on every export -- the dominant export cost.
                var cacheKey = RemoteImageCacheKey(imageUrl);
                Directory.CreateDirectory(outputDirectory);
                var cachedHit = FindCachedRemoteImage(outputDirectory, cacheKey);
                if (cachedHit != null)
                {
                    return cachedHit;
                }

                // Offline export: never hit the network -- a missing cache entry
                // means the image is simply skipped.
                if (ExportRuntimeOptions.OfflineImagesOnly)
                {
                    return null;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                request.Headers.TryAddWithoutValidation("User-Agent", "ExportAzureWiki/1.0");
                request.Headers.TryAddWithoutValidation("Accept", "image/*,*/*;q=0.8");
                using var response = _remoteImageClient.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    LoggingService.LogWarning(LocalizationManager.Sf(
                        "export.word.log.remote_image_http_fail",
                        (int)response.StatusCode,
                        imageUrl));
                    return null;
                }

                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (bytes == null || bytes.Length == 0)
                {
                    LoggingService.LogWarning(LocalizationManager.Sf(
                        "export.word.log.remote_image_empty",
                        imageUrl));
                    return null;
                }

                var extension = ResolveImageExtension(response.Content.Headers.ContentType?.MediaType, imageUrl, bytes);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    // SVG / WebP are not embeddable by the Word image pipeline.
                    // Rasterize/transcode to PNG from the bytes we already have.
                    var convertedPng = RasterImageConverter.LooksLikeSvg(bytes)
                        ? RasterImageConverter.SvgToPng(Encoding.UTF8.GetString(bytes))
                        : RasterImageConverter.LooksLikeWebp(bytes)
                            ? RasterImageConverter.TranscodeToPng(bytes)
                            : null;
                    if (convertedPng != null)
                    {
                        var pngPath = Path.Combine(outputDirectory, $"{cacheKey}.png");
                        File.WriteAllBytes(pngPath, convertedPng);
                        return pngPath;
                    }

                    if (TryDownloadRasterFallback(imageUrl, outputDirectory, out var fallbackPath))
                    {
                        return fallbackPath;
                    }

                    LoggingService.LogWarning(LocalizationManager.Sf(
                        "export.word.log.remote_image_ext_unknown",
                        imageUrl,
                        response.Content.Headers.ContentType?.MediaType ?? string.Empty,
                        bytes.Length));
                    return null;
                }

                var filePath = Path.Combine(outputDirectory, $"{cacheKey}.{extension}");
                File.WriteAllBytes(filePath, bytes);
                return filePath;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(LocalizationManager.Sf(
                    "export.word.log.remote_image_exception",
                    imageUrl), ex);
                return null;
            }
        }

        private static bool TryDownloadRasterFallback(string imageUrl, string outputDirectory, out string fallbackPath)
        {
            fallbackPath = string.Empty;
            try
            {
                // Offline export never reaches out to the shields.io PNG endpoint.
                if (ExportRuntimeOptions.OfflineImagesOnly)
                {
                    return false;
                }

                if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                if (!uri.Host.Contains("img.shields.io", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var absolute = uri.GetLeftPart(UriPartial.Path);
                if (absolute.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var pngUrl = $"{absolute}.png{uri.Query}";
                var cacheKey = RemoteImageCacheKey(pngUrl);
                Directory.CreateDirectory(outputDirectory);
                var cachedHit = FindCachedRemoteImage(outputDirectory, cacheKey);
                if (cachedHit != null)
                {
                    fallbackPath = cachedHit;
                    return true;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, pngUrl);
                request.Headers.TryAddWithoutValidation("User-Agent", "ExportAzureWiki/1.0");
                request.Headers.TryAddWithoutValidation("Accept", "image/png,image/*;q=0.8,*/*;q=0.5");
                using var response = _remoteImageClient.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (bytes == null || bytes.Length == 0)
                {
                    return false;
                }

                var filePath = Path.Combine(outputDirectory, $"{cacheKey}.png");
                File.WriteAllBytes(filePath, bytes);
                fallbackPath = filePath;
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(LocalizationManager.Sf(
                    "export.word.log.remote_image_fallback_exception",
                    imageUrl), ex);
                return false;
            }
        }

        private static string? TryPersistDataUriImage(string dataUri, string outputDirectory)
        {
            try
            {
                var match = Regex.Match(dataUri, @"^data:image/(?<type>[a-zA-Z0-9\+\-\.]+);base64,(?<data>.+)$");
                if (!match.Success)
                {
                    return null;
                }

                var base64 = match.Groups["data"].Value;
                var bytes = Convert.FromBase64String(base64);
                var mediaType = match.Groups["type"].Value.ToLowerInvariant();

                // SVG / WebP cannot be embedded directly: convert to PNG bytes.
                if (mediaType == "svg+xml")
                {
                    bytes = RasterImageConverter.SvgToPng(Encoding.UTF8.GetString(bytes))!;
                }
                else if (mediaType == "webp")
                {
                    bytes = RasterImageConverter.TranscodeToPng(bytes)!;
                }

                var extension = mediaType switch
                {
                    "jpeg" => "jpg",
                    "svg+xml" => "png",
                    "webp" => "png",
                    _ => mediaType
                };
                if (string.IsNullOrWhiteSpace(extension) || bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                var filePath = Path.Combine(outputDirectory, $"img_{Guid.NewGuid():N}.{extension}");
                File.WriteAllBytes(filePath, bytes);
                return filePath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Rasterizes a single inline SVG element's markup to a PNG file and
        /// returns its path, or null on failure (caller then drops the SVG).
        /// </summary>
        private static string? TryRasterizeInlineSvg(string svgXml, string outputDirectory)
        {
            var png = RasterImageConverter.SvgToPng(svgXml);
            if (png == null)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
                var path = Path.Combine(outputDirectory, $"svg_{Guid.NewGuid():N}.png");
                File.WriteAllBytes(path, png);
                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Durable cache for images embedded in Word exports (remote badges,
        /// data-URI images). Lives under the same Cache root as the render and
        /// wiki-image caches -- not %TEMP%, which Disk Cleanup can wipe.
        /// </summary>
        private static string ResolveWordImagesCacheFolderPath()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ExportAzureWiki",
                "Cache",
                "WordImages");
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Empties the durable Word image cache so the next export re-downloads
        /// remote images (e.g. shields.io badges) fresh. Wired to the existing
        /// "refresh cache before export" option.
        /// </summary>
        public static void ClearWordImageCache()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(ResolveWordImagesCacheFolderPath()))
                {
                    try { File.Delete(file); } catch { /* best effort */ }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Failed to clear Word image cache", ex);
            }
        }

        private static string ResolveWikiImagesFolderPath()
        {
            var localCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ExportAzureWiki",
                "Cache",
                "WikiImages");

            if (Directory.Exists(localCachePath))
            {
                return localCachePath;
            }

            // Application.StartupPath was a WinForms accessor; AppContext.BaseDirectory
            // is the UI-stack-neutral equivalent and works the same way on the deployed
            // exe path.
            return Path.Combine(AppContext.BaseDirectory, "WikiImages");
        }

        // Matches the cached image URLs the renderer emits, e.g.
        // https://local.images/<scope>/<file>.png -- capturing the path after the host.
        private static readonly Regex LocalImageUrlRegex =
            new(@"https://local\.images/([^""')\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The on-disk image cache is encrypted at rest, but the Word
        /// embedder (OpenXML FileStream plus SVG/WebP transcode) reads raw files from disk.
        /// This decrypts every image
        /// referenced by <paramref name="htmlContent"/> into a fresh throwaway
        /// folder under the managed cache root and returns that folder. The
        /// caller rewrites <c>https://local.images/...</c> URLs to point here and
        /// must delete it (see <see cref="TryDeleteImageWorkspace"/>) once the
        /// export has been written. Plaintext therefore exists only transiently,
        /// during a user-initiated export, and never in the long-lived cache.
        /// </summary>
        private static string CreateDecryptedImageWorkspace(string htmlContent)
        {
            var sourceRoot = Path.GetFullPath(ResolveWikiImagesFolderPath());
            var workspace = Path.Combine(WikiCachePaths.Root, "ExportTmp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);

            if (string.IsNullOrEmpty(htmlContent))
            {
                return workspace;
            }

            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in LocalImageUrlRegex.Matches(htmlContent))
            {
                var relative = Uri.UnescapeDataString(match.Groups[1].Value).Replace('/', Path.DirectorySeparatorChar);
                if (!done.Add(relative))
                {
                    continue;
                }

                try
                {
                    var source = Path.GetFullPath(Path.Combine(sourceRoot, relative));
                    // Refuse anything that escapes the cache root (path traversal).
                    if (!source.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                    {
                        continue;
                    }

                    var destination = Path.Combine(workspace, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, SecureCacheFile.ReadBytes(source));
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning($"Failed to materialize cached image for export: {relative}", ex);
                }
            }

            return workspace;
        }

        private static void TryDeleteImageWorkspace(string workspace)
        {
            try
            {
                if (Directory.Exists(workspace))
                {
                    Directory.Delete(workspace, recursive: true);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Failed to delete decrypted image workspace", ex);
            }
        }

        private static void SanitizeSvgNode(HtmlNode svg)
        {
            var nodes = new List<HtmlNode> { svg };
            var descendants = svg.SelectNodes(".//*");
            if (descendants != null)
            {
                nodes.AddRange(descendants);
            }

            foreach (var node in nodes)
            {
                if (!node.HasAttributes)
                {
                    continue;
                }

                foreach (var attr in node.Attributes.ToList())
                {
                    var value = (attr.Value ?? string.Empty).Trim();
                    if (attr.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            node.Attributes.Remove(attr);
                            continue;
                        }

                        var sanitizedStyle = Regex.Replace(
                            value,
                            @"(?i)(width|height|x|y|rx|ry|r|cx|cy|stroke-width|font-size)\s*:\s*(@[a-z0-9_\-]+|auto)\b",
                            "$1:0");
                        sanitizedStyle = Regex.Replace(sanitizedStyle, @"@[a-zA-Z0-9_\-]+", "0");
                        attr.Value = sanitizedStyle;
                        continue;
                    }

                    if (string.IsNullOrEmpty(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("@null", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("@null", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("@auto", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("@", StringComparison.Ordinal))
                    {
                        if (node.Name.Equals("rect", StringComparison.OrdinalIgnoreCase) &&
                            (attr.Name.Equals("width", StringComparison.OrdinalIgnoreCase) ||
                             attr.Name.Equals("height", StringComparison.OrdinalIgnoreCase) ||
                             attr.Name.Equals("x", StringComparison.OrdinalIgnoreCase) ||
                             attr.Name.Equals("y", StringComparison.OrdinalIgnoreCase)))
                        {
                            attr.Value = "0";
                        }
                        else
                        {
                            node.Attributes.Remove(attr);
                        }
                    }
                }
            }

            EnsureNumericAttr(svg, "width", "1200");
            EnsureNumericAttr(svg, "height", "800");

            var rects = svg.SelectNodes(".//rect");
            if (rects == null)
            {
                return;
            }

            foreach (var rect in rects)
            {
                EnsureNumericAttr(rect, "width", "0");
                EnsureNumericAttr(rect, "height", "0");
                EnsureNumericAttr(rect, "x", "0");
                EnsureNumericAttr(rect, "y", "0");
            }
        }

        private static void EnsureNumericAttr(HtmlNode node, string attrName, string fallback)
        {
            var raw = node.GetAttributeValue(attrName, null);
            if (string.IsNullOrWhiteSpace(raw))
            {
                node.SetAttributeValue(attrName, fallback);
                return;
            }

            var value = raw.Trim();
            if (value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("@null", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("@auto", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("@", StringComparison.Ordinal))
            {
                node.SetAttributeValue(attrName, fallback);
                return;
            }

            // Keep critical SVG dimensions numeric before rasterization.
            if (!Regex.IsMatch(value, @"^-?\d+(\.\d+)?(px|pt|pc|cm|mm|in|%)?$", RegexOptions.IgnoreCase))
            {
                node.SetAttributeValue(attrName, fallback);
            }
        }

        
    }
}
