using System.Text.RegularExpressions;

namespace ExportAzureWiki.Services;

internal static class WikiCacheScopeHelper
{
    public static string FromWikiId(string? wikiId)
    {
        if (string.IsNullOrWhiteSpace(wikiId))
        {
            return "global";
        }

        var normalized = Regex.Replace(wikiId.Trim(), @"[^a-zA-Z0-9_\-]+", "-");
        normalized = normalized.Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "global";
        }

        return $"wiki-{normalized.ToLowerInvariant()}";
    }
}

