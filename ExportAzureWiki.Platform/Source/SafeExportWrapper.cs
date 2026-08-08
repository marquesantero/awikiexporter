using System.Text.RegularExpressions;
using ExportAzureWiki.Services;

namespace ExportAzureWiki;

/// <summary>
/// Helpers for assembling exported documentation.
///
/// History: this class used to also host a WebView2-WinForms screen-capture
/// and HTML-to-PDF path (SafeCaptureHtmlAsPngSlicesAsync, SafePrintHtmlToPdfAsync,
/// SafeRenderMermaidToImageHtmlAsync, SafeProcessCombinedHtmlAsync). That path
/// belonged to the legacy WinForms UI and was replaced by the WPF shell's own
/// WebView2 (WPF) print host plus the OpenXML pipeline in ExportService.
/// Those methods were dead code and were removed in Fase 3.1b to drop the
/// Microsoft.Web.WebView2.WinForms and System.Drawing dependencies, which let
/// Platform turn off UseWindowsForms entirely.
/// </summary>
public static class SafeExportWrapper
{
    /// <summary>
    /// Streams each cached page's HTML, extracts the first page's &lt;head&gt;
    /// and every page's &lt;body&gt;, and stitches them into a single document.
    /// Reads files one at a time to keep memory flat on large exports; a page
    /// that fails to read is replaced with an inline error marker rather than
    /// aborting the whole combine.
    /// </summary>
    public static string SafeCombinePages(List<PageInfo> pageInfoList)
    {
        LoggingService.LogInfo($"Starting SafeCombinePages with {pageInfoList?.Count ?? 0} pages");
        LoggingService.LogMemoryUsage("BEFORE_COMBINE_PAGES");

        if (pageInfoList?.Count == 0)
        {
            LoggingService.LogWarning("No pages found to combine");
            return "<html><body><p>Nenhuma página encontrada.</p></body></html>";
        }

        try
        {
            var combinedBody = string.Empty;
            var headContent = string.Empty;
            var processedPages = 0;

            // Process files one by one to avoid loading all into memory.
            foreach (var pageInfo in pageInfoList!)
            {
                try
                {
                    LoggingService.LogDebug($"Processing page: {pageInfo.HtmlFilePath}");
                    // Cached HTML is encrypted at rest; decrypt in memory.
                    var pageHtml = SecureCacheFile.Decode(File.ReadAllText(pageInfo.HtmlFilePath));

                    if (string.IsNullOrEmpty(headContent))
                    {
                        var headMatch = Regex.Match(pageHtml, @"<head>([\s\S]*?)<\/head>");
                        if (headMatch.Success)
                        {
                            headContent = headMatch.Value;
                            LoggingService.LogDebug("Extracted head content from first page");
                        }
                    }

                    var bodyMatch = Regex.Match(pageHtml, @"<body[^>]*>([\s\S]*?)<\/body>");
                    if (bodyMatch.Success)
                    {
                        combinedBody += bodyMatch.Groups[1].Value;
                        processedPages++;
                    }

                    if (processedPages % 10 == 0)
                    {
                        LoggingService.LogMemoryUsage($"AFTER_{processedPages}_PAGES");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Error processing page {pageInfo.HtmlFilePath}", ex);
                    combinedBody += $"<p>Erro ao carregar página: {pageInfo.Page.Path}</p>";
                }
            }

            LoggingService.LogInfo($"Successfully combined {processedPages} pages");
            LoggingService.LogMemoryUsage("AFTER_COMBINE_PAGES");

            var result = $"""
                    <html>
                    {headContent}
                    <body class='content-body'>
                    {combinedBody}
                    </body>
                    </html>
                    """;

            LoggingService.LogInfo($"Final combined HTML length: {result.Length}");
            return result;
        }
        catch (Exception ex)
        {
            LoggingService.LogCritical("Fatal error combining pages", ex);
            return "<html><body><p>Erro ao processar documentação.</p></body></html>";
        }
    }
}
