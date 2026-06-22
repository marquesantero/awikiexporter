using System.Text.RegularExpressions;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using HtmlNode = HtmlAgilityPack.HtmlNode;
using HtmlNodeType = HtmlAgilityPack.HtmlNodeType;

namespace ExportAzureWiki.Services;

/// <summary>
/// Strips active content (script execution, event handlers, dangerous URI
/// schemes) from wiki-sourced HTML before it is embedded in the WebView2
/// host page or piped into the export pipeline.
///
/// Markdig renders wiki Markdown to HTML, but Markdig in advanced mode
/// happily passes through any raw HTML the author embedded. That means a
/// wiki page can ship <c>&lt;img src=x onerror=fetch(...)&gt;</c> straight
/// into the WebView2 surface, which has elevated host integration. This
/// sanitizer collapses the obvious vectors before render.
///
/// This is *not* a general-purpose XSS sanitizer. It is intentionally
/// conservative -- pair it with a strict CSP on the host page.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly HashSet<string> DisallowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "iframe", "frame", "frameset", "object", "embed",
        "applet", "form", "input", "button", "textarea", "select",
        "link", "meta", "base"
    };

    private static readonly HashSet<string> DangerousUrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "action", "formaction", "background", "poster", "xlink:href"
    };

    // javascript:, vbscript:, file: end in the colon that the test below
    // checks. data:text/html already contains the colon inside the prefix
    // itself, so it must be matched without requiring another one.
    [GeneratedRegex(@"^\s*(javascript:|vbscript:|file:|data:text/html)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousUrlPrefix();

    [GeneratedRegex(@"expression\s*\(|@import|behavior\s*:|javascript\s*:|vbscript\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousCssPattern();

    /// <summary>
    /// Returns the HTML stripped of active-content risks.
    /// </summary>
    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html ?? string.Empty;
        }

        var doc = new HtmlDocument
        {
            OptionAutoCloseOnEnd = true,
            OptionFixNestedTags = true
        };
        doc.LoadHtml(html);

        SanitizeNode(doc.DocumentNode);

        return doc.DocumentNode.InnerHtml;
    }

    private static void SanitizeNode(HtmlNode node)
    {
        // Snapshot children first because we mutate during iteration.
        var children = node.ChildNodes.ToList();
        foreach (var child in children)
        {
            if (child.NodeType != HtmlNodeType.Element)
            {
                continue;
            }

            if (DisallowedTags.Contains(child.Name))
            {
                child.Remove();
                continue;
            }

            SanitizeAttributes(child);
            SanitizeNode(child);
        }
    }

    private static void SanitizeAttributes(HtmlNode element)
    {
        var attributes = element.Attributes.ToList();
        foreach (var attribute in attributes)
        {
            var name = attribute.Name;

            // Drop every inline event handler. Even attributes that look
            // benign (onload on a normal element) execute on render.
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes.Remove(attribute);
                continue;
            }

            // srcdoc embeds a new HTML document context; we cannot reason
            // about it here, so drop it.
            if (string.Equals(name, "srcdoc", StringComparison.OrdinalIgnoreCase))
            {
                element.Attributes.Remove(attribute);
                continue;
            }

            if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
            {
                if (DangerousCssPattern().IsMatch(attribute.Value ?? string.Empty))
                {
                    element.Attributes.Remove(attribute);
                }
                continue;
            }

            if (DangerousUrlAttributes.Contains(name))
            {
                var value = attribute.Value ?? string.Empty;
                if (DangerousUrlPrefix().IsMatch(value))
                {
                    element.Attributes.Remove(attribute);
                }
            }
        }
    }
}
