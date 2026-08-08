using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Services;
using System.Text.RegularExpressions;

namespace ExportAzureWiki.Platform.Backend;

internal sealed class ExportBackend : IExportBackend
{
    private readonly IExportService _exportService;
    private readonly HtmlPipelineBackend _pipeline;

    public ExportBackend()
        : this(new ExportService(), new HtmlPipelineBackend())
    {
    }

    internal ExportBackend(IExportService exportService, HtmlPipelineBackend pipeline)
    {
        _exportService = exportService;
        _pipeline = pipeline;
    }

    public Task ExportToWordAsync(string html, string filePath, bool applyWordFineTune = false, bool refreshImageCache = false)
    {
        // The whole conversion (Chromium pipeline post-processing + the
        // synchronous OpenXML ExportToWord) is CPU-bound and has no UI
        // affinity (the pipeline drives Playwright out-of-process). Running
        // it on the thread pool keeps the WPF dispatcher free; without this,
        // every await continuation resumes on the UI thread (captured
        // SynchronizationContext) and the OpenXML work freezes the window
        // for the duration of the export.
        return Task.Run(async () =>
        {
            try
            {
                // Honour the existing "refresh cache before export" option for
                // remote images too: wipe the durable image cache so badges and
                // other remote images are re-fetched fresh.
                if (refreshImageCache)
                {
                    ExportService.ClearWordImageCache();
                }

                var preparedHtml = await _pipeline.PrepareForWordExportAsync(html).ConfigureAwait(false) ?? string.Empty;
                var mermaidImgCount = Regex.Matches(preparedHtml, "<img[^>]+(?:Mermaid diagram|mermaid)", RegexOptions.IgnoreCase).Count;
                var mermaidDivCount = Regex.Matches(preparedHtml, "<div[^>]+class\\s*=\\s*['\\\"][^'\\\"]*mermaid", RegexOptions.IgnoreCase).Count;
                var mermaidCodeCount = Regex.Matches(preparedHtml, "language-mermaid", RegexOptions.IgnoreCase).Count;
                var mathImgCount = Regex.Matches(preparedHtml, "<img[^>]+data-awiki-math\\s*=\\s*['\\\"]1['\\\"]", RegexOptions.IgnoreCase).Count;
                LoggingService.LogInfo($"PLATFORM_WORD_PREPARED_HTML: mermaidImg={mermaidImgCount}; mermaidDiv={mermaidDivCount}; mermaidCode={mermaidCodeCount}; mathImg={mathImgCount}; length={preparedHtml.Length}");
                _exportService.ExportToWord(preparedHtml, filePath, null, applyWordFineTune);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"PLATFORM_WORD_EXPORT_ERROR: {ex}");
                throw;
            }
        });
    }
}







