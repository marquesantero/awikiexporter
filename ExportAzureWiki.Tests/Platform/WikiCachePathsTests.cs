using ExportAzureWiki;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Guards the data-at-rest location: every app cache path must live under the
/// single app-owned LocalAppData root and never under the system TEMP folder.
/// </summary>
public sealed class WikiCachePathsTests
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void Root_IsUnderLocalAppData_NotTemp()
    {
        WikiCachePaths.Root.Should().StartWith(Path.Combine(LocalAppData, "ExportAzureWiki", "Cache"));
        WikiCachePaths.Root.Should().NotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void AllSubPaths_StayUnderRoot()
    {
        var paths = new[]
        {
            WikiCachePaths.Pages("scope"),
            WikiCachePaths.Images("scope"),
            WikiCachePaths.Source("scope"),
            WikiCachePaths.Clone("key"),
            WikiCachePaths.ZipImport("id"),
            WikiCachePaths.WebView2,
        };

        paths.Should().OnlyContain(p => p.StartsWith(WikiCachePaths.Root, StringComparison.Ordinal));
    }

    [Fact]
    public void NoPath_TouchesSystemTemp()
    {
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var paths = new[]
        {
            WikiCachePaths.Pages("s"),
            WikiCachePaths.Images("s"),
            WikiCachePaths.Source("s"),
            WikiCachePaths.Clone("k"),
            WikiCachePaths.ZipImport("i"),
            WikiCachePaths.WebView2,
        };

        paths.Should().OnlyContain(p => !p.StartsWith(temp, StringComparison.OrdinalIgnoreCase));
    }
}
