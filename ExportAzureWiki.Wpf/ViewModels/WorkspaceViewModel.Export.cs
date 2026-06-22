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
    /// reaches mermaid.ink. Blocks that fail to render fall back to a plain code
    /// block. No-op when no handler is available.
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

        var tempDir = Path.Combine(Path.GetTempPath(), "AWikiMermaid");
        Directory.CreateDirectory(tempDir);

        foreach (var (node, source) in targets)
        {
            byte[]? png = null;
            try
            {
                png = await MermaidRenderHandler(source);
            }
            catch
            {
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
            }
            else
            {
                // No external fallback: keep the diagram source as a code block.
                var pre = doc.CreateElement("pre");
                var code = doc.CreateElement("code");
                code.InnerHtml = System.Net.WebUtility.HtmlEncode(source);
                pre.AppendChild(code);
                node.ParentNode.ReplaceChild(pre, node);
            }
        }

        return doc.DocumentNode.OuterHtml;
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
        var html = await BuildExportHtmlAsync(IncludeAdditionalPages, SelectedScope, RefreshCacheBeforeExport);
        if (string.IsNullOrWhiteSpace(html))
        {
            Status = AppText.S("wpf.export.status.no_content", "No page selected/content loaded.");
            return;
        }

        html = await PrerenderMermaidAsync(html);

        var dialog = new SaveFileDialog
        {
            Filter = AppText.S("wpf.export.dialog.word.filter", "Word (*.docx)|*.docx"),
            FileName = AppText.S("wpf.export.dialog.word.filename", "wiki-export.docx")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await QueueExportAsync(
            format: "word",
            outputPath: dialog.FileName,
            html: html,
            successStatusFormat: AppText.S("wpf.export.status.word_success", "Word exported: {0}"),
            busyText: AppText.S("wpf.workspace.busy.exporting_word", "Exporting Word..."),
            exportOperation: () => _documentExportService.ExportToWordAsync(html, dialog.FileName, ApplyWordFineTune && SelectedScope == ExportScope.CurrentDocument, RefreshCacheBeforeExport));
    }

    private async Task ExportPdfAsync()
    {
        var html = await BuildExportHtmlAsync(IncludeAdditionalPages, SelectedScope, RefreshCacheBeforeExport);
        if (string.IsNullOrWhiteSpace(html))
        {
            Status = AppText.S("wpf.export.status.no_content", "No page selected/content loaded.");
            return;
        }

        html = await PrerenderMermaidAsync(html);

        var dialog = new SaveFileDialog
        {
            Filter = AppText.S("wpf.export.dialog.pdf.filter", "PDF (*.pdf)|*.pdf"),
            FileName = AppText.S("wpf.export.dialog.pdf.filename", "wiki-export.pdf")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await QueueExportAsync(
            format: "pdf",
            outputPath: dialog.FileName,
            html: html,
            successStatusFormat: AppText.S("wpf.export.status.pdf_success", "PDF exported: {0}"),
            busyText: AppText.S("wpf.workspace.busy.exporting_pdf", "Exporting PDF..."),
            exportOperation: async () =>
            {
                var exported = false;
                if (PdfPrintHandlerAsync != null)
                {
                    exported = await PdfPrintHandlerAsync(html, dialog.FileName);
                }

                if (!exported)
                {
                    await _documentExportService.ExportToPdfAsync(html, dialog.FileName);
                }
            });
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
            // Flows into the export's async chain (and its Task.Run): when on,
            // remote images come only from cache and nothing hits the network.
            ExportRuntimeOptions.OfflineImagesOnly = OfflineExport;
            await exportOperation();
            success = true;
            Status = string.Format(successStatusFormat, outputPath);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            Status = string.Format(AppText.S("wpf.export.status.error", "Error: {0}"), ex.Message);
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
                DetailsJson = $"{{\"includeAdditionalPages\":{IncludeAdditionalPages.ToString().ToLowerInvariant()},\"refreshCache\":{RefreshCacheBeforeExport.ToString().ToLowerInvariant()},\"applyWordFineTune\":{ApplyWordFineTune.ToString().ToLowerInvariant()}}}"
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
