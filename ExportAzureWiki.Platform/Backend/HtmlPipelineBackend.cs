using System.Net;
using HtmlAgilityPack;
using ExportAzureWiki.Services;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using System.Text.RegularExpressions;

namespace ExportAzureWiki.Platform.Backend;

internal sealed class HtmlPipelineBackend
{
    public async Task<string> PrepareForWordExportAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        var hasMath = Regex.IsMatch(
            html,
            @"\\\(|\\\[|\$\$|<span[^>]*\bclass\s*=\s*['""][^'""]*\bmath\b|<div[^>]*\bclass\s*=\s*['""][^'""]*\bmath\b",
            RegexOptions.IgnoreCase);
        LoggingService.LogInfo($"WORD_MATH_PIPELINE_STAGE=PREPARE_HTML; hasMath={hasMath}");

        var highlightedHtml = await ExportChromiumPipelineService.TryProcessCombinedHtmlAsync(html).ConfigureAwait(false);
        return MergeHighlightedHtmlWithOriginalMermaidSource(html, highlightedHtml);
    }

    public async Task<string> PrepareForPdfExportAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        var processed = await ExportChromiumPipelineService.TryProcessCombinedHtmlAsync(html).ConfigureAwait(false);
        processed = await ExportChromiumPipelineService.TryRenderMermaidAndInlineImagesAsync(processed).ConfigureAwait(false);
        return processed;
    }

    private static string MergeHighlightedHtmlWithOriginalMermaidSource(string originalHtml, string highlightedHtml)
    {
        if (string.IsNullOrWhiteSpace(highlightedHtml))
        {
            return originalHtml;
        }

        try
        {
            var originalDoc = new HtmlDocument();
            originalDoc.LoadHtml(originalHtml);

            var highlightedDoc = new HtmlDocument();
            highlightedDoc.LoadHtml(highlightedHtml);

            var originalMermaidSources = originalDoc.DocumentNode
                .SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' mermaid ')]")
                ?.Select(node => WebUtility.HtmlDecode(node.InnerText ?? string.Empty).Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList() ?? [];

            if (originalMermaidSources.Count == 0)
            {
                return highlightedHtml;
            }

            var highlightedMermaidDivs = highlightedDoc.DocumentNode
                .SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' mermaid ')]")
                ?.ToList() ?? [];

            var limit = Math.Min(originalMermaidSources.Count, highlightedMermaidDivs.Count);
            for (var i = 0; i < limit; i++)
            {
                highlightedMermaidDivs[i].RemoveAllChildren();
                highlightedMermaidDivs[i].InnerHtml = WebUtility.HtmlEncode(originalMermaidSources[i]);
            }

            return highlightedDoc.DocumentNode.OuterHtml;
        }
        catch
        {
            return highlightedHtml;
        }
    }
}




