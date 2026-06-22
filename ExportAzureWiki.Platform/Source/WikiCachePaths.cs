using System.IO;

namespace ExportAzureWiki;

/// <summary>
/// Single source of truth for every on-disk cache the app owns. Everything
/// lives under one app-managed root in the user's LocalAppData
/// (<c>%LOCALAPPDATA%\ExportAzureWiki\Cache</c>) -- never in the system TEMP
/// folder, which is shared, OS-managed, and may be wiped by cleanup tools at
/// any time. Keeping a single root lets the app reason about (and wipe) the
/// whole footprint, and is where the at-rest encryption applies.
/// </summary>
public static class WikiCachePaths
{
    /// <summary>The app-owned cache root: <c>%LOCALAPPDATA%\ExportAzureWiki\Cache</c>.</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExportAzureWiki",
        "Cache");

    /// <summary>Rendered HTML pages, per wiki scope.</summary>
    public static string Pages(string scope) => Path.Combine(Root, "WikiPages", scope);

    /// <summary>Images referenced by rendered pages, per wiki scope.</summary>
    public static string Images(string scope) => Path.Combine(Root, "WikiImages", scope);

    /// <summary>Raw source Markdown cache (offline reuse), per wiki scope.</summary>
    public static string Source(string scope) => Path.Combine(Root, "WikiSource", scope);

    /// <summary>Transient git working tree for clone-based wiki sources.</summary>
    public static string Clone(string key) => Path.Combine(Root, "WikiClone", key);

    /// <summary>Extraction target for imported wiki .zip archives.</summary>
    public static string ZipImport(string id) => Path.Combine(Root, "ZipImport", id);

    /// <summary>Dedicated WebView2 user-data folder (kept out of the default location).</summary>
    public static string WebView2 => Path.Combine(Root, "WebView2");
}
