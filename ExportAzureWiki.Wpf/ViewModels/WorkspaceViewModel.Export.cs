using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Input;
using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Wpf.Commands;
using ExportAzureWiki.Wpf.Services;
using Markdig;
using HtmlAgilityPack;
using Microsoft.Win32;
using ExportAzureWiki;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public async Task<string> BuildExportHtmlAsync(bool includeAdditionalPages, ExportScope scope, bool forceRefreshCache = false)
    {
        if (scope == ExportScope.CurrentDocument)
        {
            if (forceRefreshCache)
            {
                await RefreshRenderedContentForThemeAsync();
            }

            var currentHtml = CurrentPageHtml;
            if (string.IsNullOrWhiteSpace(currentHtml) && _currentRenderedPageIndex >= 0 && _currentRenderedPageIndex < _renderedPages.Count)
            {
                var filePath = _renderedPages[_currentRenderedPageIndex].HtmlFilePath;
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    currentHtml = await SecureCacheFile.ReadTextAsync(filePath);
                }
            }

            if (string.IsNullOrWhiteSpace(currentHtml))
            {
                return string.Empty;
            }

            return includeAdditionalPages
                ? AppendAdditionalPagesToHtml(currentHtml)
                : currentHtml;
        }

        // Local folder pages are rendered lazily; an "all pages" export must
        // first render the whole set (in folder order).
        await EnsureAllLocalFolderRenderedAsync();

        var allLoadedHtml = await BuildAllLoadedPagesHtmlAsync();
        if (string.IsNullOrWhiteSpace(allLoadedHtml))
        {
            return string.Empty;
        }

        return includeAdditionalPages
            ? AppendAdditionalPagesToHtml(allLoadedHtml)
            : allLoadedHtml;
    }

    /// <summary>
    /// Replaces every Mermaid block in the export HTML with a locally rendered
    /// PNG (via <see cref="MermaidRenderHandler"/>), so Word/PDF export never
    /// reaches mermaid.ink. Blocks that fail to render stay as Mermaid blocks so
    /// the backend Chromium export pipeline can still render them.
    /// </summary>
    private async Task<string> PrerenderMermaidAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || MermaidRenderHandler == null)
        {
            return html;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var targets = new List<(HtmlNode Node, string Source)>();

        var divs = doc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' mermaid ')]");
        if (divs != null)
        {
            foreach (var div in divs)
            {
                if (div.SelectSingleNode(".//img") != null || div.SelectSingleNode(".//svg") != null)
                {
                    continue;
                }

                var source = System.Net.WebUtility.HtmlDecode(
                    div.GetAttributeValue("data-mermaid-source", null) ?? div.InnerText ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(source))
                {
                    targets.Add((div, source));
                }
            }
        }

        var pres = doc.DocumentNode.SelectNodes(
            "//pre[.//code[contains(@class,'language-mermaid') or contains(@class,'mermaid')]]");
        if (pres != null)
        {
            foreach (var pre in pres)
            {
                var code = pre.SelectSingleNode(".//code");
                var source = System.Net.WebUtility.HtmlDecode(code?.InnerText ?? pre.InnerText ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(source))
                {
                    targets.Add((pre, source));
                }
            }
        }

        if (targets.Count == 0)
        {
            return html;
        }

        LoggingService.LogInfo($"WPF_MERMAID_PRERENDER_START: count={targets.Count}");
        var tempDir = Path.Combine(Path.GetTempPath(), "AWikiMermaid");
        Directory.CreateDirectory(tempDir);

        var renderedCount = 0;
        var fallbackCount = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var (node, source) = targets[i];
            byte[]? png = null;
            try
            {
                png = await TryRenderMermaidWithTimeoutAsync(source, i + 1, targets.Count);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning($"WPF_MERMAID_PRERENDER_FAILED: index={i + 1}; error={ex.Message}");
                png = null;
            }

            if (png is { Length: > 0 })
            {
                var file = Path.Combine(tempDir, $"mermaid_{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(file, png);
                var img = doc.CreateElement("img");
                img.SetAttributeValue("src", new Uri(file).AbsoluteUri);
                img.SetAttributeValue("alt", "Mermaid diagram");
                node.ParentNode.ReplaceChild(img, node);
                renderedCount++;
            }
            else
            {
                var div = doc.CreateElement("div");
                div.SetAttributeValue("class", "mermaid");
                div.SetAttributeValue("data-mermaid-source", System.Net.WebUtility.HtmlEncode(source));
                div.InnerHtml = System.Net.WebUtility.HtmlEncode(source);
                node.ParentNode.ReplaceChild(div, node);
                fallbackCount++;
            }
        }

        LoggingService.LogInfo($"WPF_MERMAID_PRERENDER_DONE: rendered={renderedCount}; fallback={fallbackCount}");
        return doc.DocumentNode.OuterHtml;
    }

    private async Task<byte[]?> TryRenderMermaidWithTimeoutAsync(string source, int index, int total)
    {
        if (MermaidRenderHandler == null)
        {
            return null;
        }

        var renderTask = MermaidRenderHandler(source);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
        var completed = await Task.WhenAny(renderTask, timeoutTask);
        if (completed == timeoutTask)
        {
            LoggingService.LogWarning($"WPF_MERMAID_PRERENDER_TIMEOUT: index={index}; total={total}");
            return null;
        }

        return await renderTask;
    }

    private Task<string> BuildAllLoadedPagesHtmlAsync()
    {
        if (_renderedPages.Count == 0)
        {
            return Task.FromResult(string.Empty);
        }

        // In local-folder mode, emit pages in folder order (not lazy-render
        // order) so the exported document follows the tree.
        IEnumerable<RenderedWikiPage> source = _renderedPages;
        if (_localFolderMode)
        {
            var byPath = _renderedPages
                .Where(p => !string.IsNullOrWhiteSpace(p.Path))
                .GroupBy(p => p.Path!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var checkedPaths = GetCheckedLocalPaths();
            source = _localFolderOrder
                .Where(checkedPaths.Contains)
                .Where(byPath.ContainsKey)
                .Select(rel => byPath[rel]);
        }

        var pages = source
            .Where(p => !string.IsNullOrWhiteSpace(p.HtmlFilePath) && File.Exists(p.HtmlFilePath))
            .Select(p => new PageInfo
            {
                HtmlFilePath = p.HtmlFilePath,
                Page = new AzureDevOpsService.LocalWikiPage
                {
                    Path = p.Path,
                    SubPages = new List<AzureDevOpsService.LocalWikiPage>()
                }
            })
            .ToList();

        if (pages.Count == 0)
        {
            return Task.FromResult(string.Empty);
        }

        // SafeCombinePages reads every cached page file and concatenates the
        // bodies -- pure CPU/IO with no UI affinity. Task.FromResult ran it
        // synchronously on the dispatcher and froze the window on large
        // exports; the thread pool keeps the UI responsive.
        return Task.Run(() => SafeExportWrapper.SafeCombinePages(pages));
    }

    private static IEnumerable<string> FlattenTreePaths(IEnumerable<WikiPageNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Path))
            {
                yield return node.Path;
            }

            foreach (var child in FlattenTreePaths(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string ExtractTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return System.Net.WebUtility.HtmlDecode(doc.DocumentNode.InnerText ?? string.Empty).Trim();
    }

    private string AppendAdditionalPagesToHtml(string html)
    {
        if (AdditionalExportPages.Count == 0)
        {
            return html;
        }

        var bodySections = new List<string>();
        foreach (var page in AdditionalExportPages)
        {
            bodySections.Add($"<h1>{System.Net.WebUtility.HtmlEncode(page.Title)}</h1>");
            bodySections.Add(page.HtmlFragment);
            bodySections.Add("<hr/>");
        }

        var extra = string.Join(Environment.NewLine, bodySections);
        var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return html.Insert(idx, extra);
        }

        return html + extra;
    }

    private async Task ExportWordAsync()
    {
        try
        {
            SetExportPhase(AppText.S("wpf.export.status.preparing_word", "Preparing Word export..."));
            LoggingService.LogInfo($"WPF_WORD_EXPORT_PREPARE: scope={SelectedScope}; renderedPages={_renderedPages.Count}; currentIndex={_currentRenderedPageIndex}; hasCurrentHtml={!string.IsNullOrWhiteSpace(CurrentPageHtml)}");

            var html = await BuildExportHtmlAsync(IncludeAdditionalPages, SelectedScope, RefreshCacheBeforeExport);
            if (string.IsNullOrWhiteSpace(html))
            {
                Status = AppText.S("wpf.export.status.no_content", "No page selected/content loaded.");
                LoggingService.LogWarning("WPF_WORD_EXPORT_NO_CONTENT");
                ClearExportPhase();
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = AppText.S("wpf.export.dialog.word.filter", "Word (*.docx)|*.docx"),
                FileName = BuildDefaultExportFileName("docx")
            };

            if (dialog.ShowDialog() != true)
            {
                Status = AppText.S("wpf.export.status.canceled", "Export canceled.");
                LoggingService.LogInfo("WPF_WORD_EXPORT_CANCELED");
                ClearExportPhase();
                return;
            }

            await QueueExportAsync(
                format: "word",
                outputPath: dialog.FileName,
                html: html,
                successStatusFormat: AppText.S("wpf.export.status.word_success", "Word exported: {0}"),
                busyText: AppText.S("wpf.workspace.busy.exporting_word", "Rendering content and exporting Word..."),
                exportOperation: () => _documentExportService.ExportToWordAsync(html, dialog.FileName, applyWordFineTune: false, refreshImageCache: RefreshCacheBeforeExport));
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.export.status.error", "Error: {0}"), ex.Message);
            LoggingService.LogError($"WPF_WORD_EXPORT_PRE_QUEUE_ERROR: {ex}");
            ClearExportPhase();
        }
    }

    private async Task ExportPdfAsync()
    {
        try
        {
            SetExportPhase(AppText.S("wpf.export.status.preparing_pdf", "Preparing PDF export..."));
            LoggingService.LogInfo($"WPF_PDF_EXPORT_PREPARE: scope={SelectedScope}; renderedPages={_renderedPages.Count}; currentIndex={_currentRenderedPageIndex}; hasCurrentHtml={!string.IsNullOrWhiteSpace(CurrentPageHtml)}");

            var html = await BuildExportHtmlAsync(IncludeAdditionalPages, SelectedScope, RefreshCacheBeforeExport);
            if (string.IsNullOrWhiteSpace(html))
            {
                Status = AppText.S("wpf.export.status.no_content", "No page selected/content loaded.");
                LoggingService.LogWarning("WPF_PDF_EXPORT_NO_CONTENT");
                ClearExportPhase();
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = AppText.S("wpf.export.dialog.pdf.filter", "PDF (*.pdf)|*.pdf"),
                FileName = BuildDefaultExportFileName("pdf")
            };

            if (dialog.ShowDialog() != true)
            {
                Status = AppText.S("wpf.export.status.canceled", "Export canceled.");
                LoggingService.LogInfo("WPF_PDF_EXPORT_CANCELED");
                ClearExportPhase();
                return;
            }

            await QueueExportAsync(
                format: "pdf",
                outputPath: dialog.FileName,
                html: html,
                successStatusFormat: AppText.S("wpf.export.status.pdf_success", "PDF exported: {0}"),
                busyText: AppText.S("wpf.workspace.busy.printing_pdf", "Printing PDF..."),
                exportOperation: async () =>
                {
                    if (PdfPrintHandlerAsync == null)
                    {
                        throw new InvalidOperationException(AppText.S(
                            "wpf.export.pdf_print_unavailable",
                            "PDF print layout is unavailable because the print host is not initialized."));
                    }

                    var printed = await PdfPrintHandlerAsync(html, dialog.FileName);
                    if (!printed)
                    {
                        throw new InvalidOperationException(AppText.S(
                            "wpf.export.pdf_print_failed",
                            "PDF print layout export failed."));
                    }
                });
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.export.status.error", "Error: {0}"), ex.Message);
            LoggingService.LogError($"WPF_PDF_EXPORT_PRE_QUEUE_ERROR: {ex}");
            ClearExportPhase();
        }
    }

    private string BuildDefaultExportFileName(string extension)
    {
        var source = !string.IsNullOrWhiteSpace(CurrentDocumentTitle)
            ? CurrentDocumentTitle
            : AppText.S("wpf.export.dialog.default_basename", "wiki-export");

        var name = Path.GetFileNameWithoutExtension(source.Replace('\\', '/').Split('/').LastOrDefault() ?? source);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "wiki-export";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "wiki-export";
        }

        return $"{name}.{extension.TrimStart('.')}";
    }

    private async Task QueueExportAsync(
        string format,
        string outputPath,
        string html,
        string successStatusFormat,
        string busyText,
        Func<Task> exportOperation)
    {
        QueuedExportCount += 1;
        if (QueuedExportCount > 1)
        {
            Status = string.Format(
                AppText.S("wpf.export.status.queued_position", "Export queued ({0})."),
                QueuedExportCount - 1);
        }

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var success = false;
        string? errorMessage = null;

        await _exportQueueGate.WaitAsync();
        try
        {
            IsExporting = true;
            BusyMessage = busyText;
            Status = busyText;
            LoggingService.LogInfo($"WPF_EXPORT_START: format={format}; scope={SelectedScope}; output='{outputPath}'; htmlLength={html.Length}");
            // Flows into the export's async chain (and its Task.Run): when on,
            // remote images come only from cache and nothing hits the network.
            ExportRuntimeOptions.OfflineImagesOnly = OfflineExport;
            await exportOperation();
            success = true;
            Status = string.Format(successStatusFormat, outputPath);
            LoggingService.LogInfo($"WPF_EXPORT_SUCCESS: format={format}; output='{outputPath}'; elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Status = string.Format(AppText.S("wpf.export.status.error", "Error: {0}"), ex.Message);
            LoggingService.LogError($"WPF_EXPORT_ERROR: format={format}; output='{outputPath}'; error={ex}");
        }
        finally
        {
            stopwatch.Stop();
            ExportRuntimeOptions.OfflineImagesOnly = false;
            _exportQueueGate.Release();
            QueuedExportCount = Math.Max(0, QueuedExportCount - 1);

            if (QueuedExportCount == 0)
            {
                IsExporting = false;
                BusyMessage = string.Empty;
            }
            else
            {
                BusyMessage = string.Format(
                    AppText.S("wpf.export.status.processing_queue", "Processing export queue ({0})..."),
                    QueuedExportCount);
            }

            await RecordExportHistoryAsync(new ExportHistoryEntry
            {
                Timestamp = startedAt,
                UserId = _authenticationService.CurrentUser?.Id,
                Username = _authenticationService.CurrentUser?.Username ?? _authenticationService.CurrentUser?.DisplayName,
                Format = format,
                Scope = SelectedScope == ExportScope.CurrentDocument ? "current" : "all_loaded",
                OutputPath = outputPath,
                SourcePages = SelectedScope == ExportScope.CurrentDocument ? 1 : Math.Max(1, _renderedPages.Count),
                Success = success,
                DurationMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                ErrorMessage = errorMessage,
                DetailsJson = $"{{\"includeAdditionalPages\":{IncludeAdditionalPages.ToString().ToLowerInvariant()},\"refreshCache\":{RefreshCacheBeforeExport.ToString().ToLowerInvariant()},\"pdfExportMode\":\"print\"}}"
            });
        }
    }

    private async Task RecordExportHistoryAsync(ExportHistoryEntry entry)
    {
        try
        {
            await _exportHistoryService.RecordAsync(entry);
        }
        catch
        {
            // History never blocks export flow.
        }
    }
}
