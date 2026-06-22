using System.IO;
using ExportAzureWiki.Services;
using Serilog;

namespace ExportAzureWiki;

/// <summary>
/// Lifecycle operations for the app-owned cache (<see cref="WikiCachePaths"/>):
/// purge everything, or purge the artifacts of a single wiki. Used to honor
/// "clear cache" and to drop a wiki's cached content when its configuration is
/// deleted, so content does not outlive the access it came from.
/// </summary>
public static class WikiCacheMaintenance
{
    /// <summary>Deletes the entire cache root.</summary>
    public static void ClearAll() => TryDeleteDirectory(WikiCachePaths.Root);

    /// <summary>
    /// Deletes the cached pages, images and source Markdown for a single wiki
    /// configuration (matched by the same scope the render pipeline uses).
    /// </summary>
    public static void ClearForWiki(string wikiId)
    {
        if (string.IsNullOrWhiteSpace(wikiId))
        {
            return;
        }

        var scope = WikiCacheScopeHelper.FromWikiId(wikiId);
        TryDeleteDirectory(WikiCachePaths.Pages(scope));
        TryDeleteDirectory(WikiCachePaths.Images(scope));
        TryDeleteDirectory(WikiCachePaths.Source(scope));
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clear cache directory {Dir}", dir);
        }
    }
}
