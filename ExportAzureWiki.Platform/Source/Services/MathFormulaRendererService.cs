using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;

namespace ExportAzureWiki.Services;

public static class MathFormulaRendererService
{
    public static async Task<string> RenderMathAsImagesAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var mathNodes = doc.DocumentNode.SelectNodes("//span[contains(concat(' ', normalize-space(@class), ' '), ' math ')] | //div[contains(concat(' ', normalize-space(@class), ' '), ' math ')]");
        if (mathNodes == null || mathNodes.Count == 0)
        {
            return html;
        }

        var tokens = new List<MathToken>();
        var tokenIndex = 0;
        foreach (var node in mathNodes.ToList())
        {
            var rawText = WebUtility.HtmlDecode(node.InnerText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                continue;
            }

            var displayMode = node.Name.Equals("div", StringComparison.OrdinalIgnoreCase) ||
                              node.GetAttributeValue("class", string.Empty).Contains("display", StringComparison.OrdinalIgnoreCase);
            var token = $"%%AWIKI_MATH_{tokenIndex++}%%";
            tokens.Add(new MathToken(token, rawText, displayMode));

            node.ParentNode.ReplaceChild(doc.CreateTextNode(token), node);
        }

        if (tokens.Count == 0)
        {
            return html;
        }

        var output = doc.DocumentNode.OuterHtml;
        var rendered = 0;
        foreach (var token in tokens)
        {
            var (dataUrl, width, height) = await ExportChromiumPipelineService.RenderMathFormulaDataUrlAsync(token.Formula, token.DisplayMode).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                continue;
            }

            width = Math.Max(8, width);
            height = Math.Max(8, height);
            var cssClass = token.DisplayMode ? "awiki-math-display" : "awiki-math-inline";
            var style = token.DisplayMode
                ? "display:block;max-width:100%;height:auto;margin:0.25em 0;"
                : "display:inline;vertical-align:middle;height:1em;width:auto;max-width:none;";
            var alt = WebUtility.HtmlEncode(token.Formula);

            var imgHtml = $"<img src=\"{dataUrl}\" data-awiki-math=\"1\" class=\"{cssClass}\" width=\"{width}\" height=\"{height}\" style=\"{style}\" alt=\"{alt}\" />";
            output = output.Replace(token.Token, imgHtml, StringComparison.Ordinal);
            rendered++;
        }

        // Clean unresolved placeholders to keep document readable.
        output = Regex.Replace(output, "%%AWIKI_MATH_\\d+%%", string.Empty, RegexOptions.CultureInvariant);
        LoggingService.LogInfo($"WORD_MATH_RENDER: found={tokens.Count}; rendered={rendered}; failed={tokens.Count - rendered}");
        return output;
    }

    private sealed record MathToken(string Token, string Formula, bool DisplayMode);
}
