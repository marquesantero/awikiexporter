using ExportAzureWiki.Services.WikiProviders;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Covers the pure URL/JSON helpers of the GitHub repo-Markdown source.
/// Network calls aren't exercised here (no live token/repo); the parsing and
/// URL shapes are where regressions would silently break fetching.
/// </summary>
public sealed class GitHubWikiServiceTests
{
    private const string TreeJson = """
    {
      "sha": "abc",
      "tree": [
        { "path": "README.md",            "type": "blob", "sha": "s1" },
        { "path": "docs/guide.md",         "type": "blob", "sha": "s2" },
        { "path": "docs/sub/deep.markdown","type": "blob", "sha": "s3" },
        { "path": "docs/image.png",        "type": "blob", "sha": "s4" },
        { "path": "docs",                  "type": "tree", "sha": "s5" },
        { "path": "src/code.cs",           "type": "blob", "sha": "s6" }
      ]
    }
    """;

    [Fact]
    public void ParseMarkdownPages_NoDocsPath_ReturnsAllMarkdownBlobs()
    {
        var pages = GitHubWikiService.ParseMarkdownPages(TreeJson, docsPath: "");

        pages.Select(p => p.Path).Should().BeEquivalentTo(
            new[] { "README.md", "docs/guide.md", "docs/sub/deep.markdown" });
    }

    [Fact]
    public void ParseMarkdownPages_WithDocsPath_FiltersToThatSubtree()
    {
        var pages = GitHubWikiService.ParseMarkdownPages(TreeJson, docsPath: "docs");

        pages.Select(p => p.Path).Should().BeEquivalentTo(
            new[] { "docs/guide.md", "docs/sub/deep.markdown" });
        pages.Should().NotContain(p => p.Path == "README.md");
    }

    [Fact]
    public void ParseMarkdownPages_IgnoresTreesAndNonMarkdown()
    {
        var pages = GitHubWikiService.ParseMarkdownPages(TreeJson, docsPath: "");
        pages.Should().NotContain(p => p.Path == "docs");          // tree
        pages.Should().NotContain(p => p.Path == "docs/image.png"); // not md
        pages.Should().NotContain(p => p.Path == "src/code.cs");
    }

    [Fact]
    public void ParseMarkdownPages_DerivesTitleFromFileName()
    {
        var pages = GitHubWikiService.ParseMarkdownPages(TreeJson, docsPath: "docs");
        pages.Single(p => p.Path == "docs/guide.md").Title.Should().Be("guide");
        pages.Single(p => p.Path == "docs/sub/deep.markdown").Title.Should().Be("deep");
    }

    [Fact]
    public void ParseMarkdownPages_EmptyOrTreeless_ReturnsEmpty()
    {
        GitHubWikiService.ParseMarkdownPages("{}", "").Should().BeEmpty();
        GitHubWikiService.ParseMarkdownPages("""{"tree":[]}""", "").Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("docs", "docs")]
    [InlineData("/docs/", "docs")]
    [InlineData("  docs/guide  ", "docs/guide")]
    public void NormalizeDocsPath_TrimsSlashesAndSpace(string input, string expected)
    {
        GitHubWikiService.NormalizeDocsPath(input).Should().Be(expected);
    }

    [Fact]
    public void BuildTreeUrl_UsesRecursiveTreesApi()
    {
        GitHubWikiService.BuildTreeUrl("octocat", "hello", "main")
            .Should().Be("https://api.github.com/repos/octocat/hello/git/trees/main?recursive=1");
    }

    [Theory]
    [InlineData("", "https://api.github.com/repos/octocat/hello/contents?ref=main")]
    [InlineData("docs", "https://api.github.com/repos/octocat/hello/contents/docs?ref=main")]
    [InlineData("/docs/folder name", "https://api.github.com/repos/octocat/hello/contents/docs/folder%20name?ref=feature%2Fdocs")]
    public void BuildContentsUrl_TargetsScopedContentsApi(string path, string expected)
    {
        var branch = path.Contains("folder name", StringComparison.Ordinal) ? "feature/docs" : "main";

        GitHubWikiService.BuildContentsUrl("octocat", "hello", branch, path).Should().Be(expected);
    }

    [Fact]
    public void BuildRawUrl_PointsAtRawGithubusercontent()
    {
        GitHubWikiService.BuildRawUrl("octocat", "hello", "main", "docs/guide.md")
            .Should().Be("https://raw.githubusercontent.com/octocat/hello/main/docs/guide.md");
    }

    [Theory]
    [InlineData("/docs/guide.md", "docs/guide.md")]
    [InlineData(@"docs\guide.md", "docs/guide.md")]
    [InlineData(" docs/folder name/guide.md ", "docs/folder%20name/guide.md")]
    public void NormalizeRepositoryPath_TrimsSlashesAndEscapesSegments(string input, string expected)
    {
        GitHubWikiService.NormalizeRepositoryPath(input).Should().Be(expected);
    }

    [Fact]
    public void BuildRawUrl_NormalizesLeadingSlashBeforeFetchingRawContent()
    {
        GitHubWikiService.BuildRawUrl("octocat", "hello", "main", "/docs/guide.md")
            .Should().Be("https://raw.githubusercontent.com/octocat/hello/main/docs/guide.md");
    }

    [Fact]
    public void ParseConfiguredStartPoints_SplitsAndNormalizesRootPath()
    {
        var points = GitHubWikiService.ParseConfiguredStartPoints(" /README.md | docs/guide.md | docs/ ");

        points.Should().Equal("docs", "docs/guide.md", "README.md");
    }

    [Fact]
    public void BuildPagesFromPaths_CreatesPagesWithoutNetworkForConfiguredMarkdownFiles()
    {
        var pages = GitHubWikiService.BuildPagesFromPaths(["/README.md", "docs/guide.md", "assets/logo.png"]);

        pages.Select(p => p.Path).Should().Equal("docs/guide.md", "README.md");
        pages.Single(p => p.Path == "README.md").Title.Should().Be("README");
    }

    [Fact]
    public void ApplyStartPointFilter_KeepsConfiguredSubtreeOnly()
    {
        var pages = GitHubWikiService.BuildPagesFromPaths([
            "README.md",
            "docs/guide.md",
            "docs/deep/page.md",
            "src/readme.md"
        ]);

        var filtered = GitHubWikiService.ApplyStartPointFilter(pages, ["docs"]);

        filtered.Select(p => p.Path).Should().Equal("docs/deep/page.md", "docs/guide.md");
    }

    [Theory]
    [InlineData(null, "Repo")]
    [InlineData("", "Repo")]
    [InlineData("repo", "Repo")]
    [InlineData("Wiki", "Wiki")]
    [InlineData(" wiki ", "Wiki")]
    public void NormalizeMode_DefaultsToRepoAndAcceptsWiki(string? input, string expected)
    {
        GitHubWikiService.NormalizeMode(input).Should().Be(expected);
    }

    [Fact]
    public void BuildWikiCloneUrl_TargetsGitHubWikiRepository()
    {
        GitWikiClone.BuildWikiCloneUrl("github.com", "octocat", "hello")
            .Should().Be("https://github.com/octocat/hello.wiki.git");
    }

    [Fact]
    public void BuildWikiCloneUrl_DoesNotDuplicateWikiSuffix()
    {
        GitWikiClone.BuildWikiCloneUrl("github.com", "octocat", "hello.wiki")
            .Should().Be("https://github.com/octocat/hello.wiki.git");
    }

    [Theory]
    [InlineData("github.com", "octocat", "hello", "https://github.com/octocat/hello.git")]
    [InlineData("github.com", "octocat", "hello.git", "https://github.com/octocat/hello.git")]
    [InlineData("github.com", "/octocat/", " hello ", "https://github.com/octocat/hello.git")]
    public void BuildRepoCloneUrl_TargetsMainRepository(string host, string owner, string repo, string expected)
    {
        GitWikiClone.BuildRepoCloneUrl(host, owner, repo).Should().Be(expected);
    }

    [Fact]
    public void IsConnectivityError_TrueForTransportFailures()
    {
        GitHubWikiService.IsConnectivityError(new HttpRequestException("boom")).Should().BeTrue();
        GitHubWikiService.IsConnectivityError(new TaskCanceledException()).Should().BeTrue();
        GitHubWikiService.IsConnectivityError(new TimeoutException()).Should().BeTrue();
        GitHubWikiService.IsConnectivityError(
            new System.Net.Sockets.SocketException()).Should().BeTrue();
        // Wrapped transport failure (HttpClient often nests these).
        GitHubWikiService.IsConnectivityError(
            new InvalidOperationException("outer", new HttpRequestException("inner"))).Should().BeTrue();
    }

    [Fact]
    public void IsConnectivityError_FalseForNonTransportErrors()
    {
        GitHubWikiService.IsConnectivityError(new InvalidOperationException()).Should().BeFalse();
        GitHubWikiService.IsConnectivityError(new System.Text.Json.JsonException()).Should().BeFalse();
    }

    [Fact]
    public void ParseWikiClonePages_EnumeratesMarkdownFilesOnly()
    {
        var root = CreateTempWikiRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "folder"));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "Home.md"), "# Home");
            File.WriteAllText(Path.Combine(root, "folder", "Deep.markdown"), "# Deep");
            File.WriteAllText(Path.Combine(root, "image.png"), "not markdown");
            File.WriteAllText(Path.Combine(root, ".git", "ignored.md"), "ignored");

            var pages = GitHubWikiService.ParseWikiClonePages(root);

            pages.Select(p => p.Path).Should().Equal("folder/Deep.markdown", "Home.md");
            pages.Select(p => p.Title).Should().Equal("Deep", "Home");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWikiFilePath_AllowsFilesInsideCloneOnly()
    {
        var root = CreateTempWikiRoot();
        try
        {
            var home = Path.Combine(root, "Home.md");
            File.WriteAllText(home, "# Home");

            GitHubWikiService.ResolveWikiFilePath(root, "Home.md").Should().Be(home);
            GitHubWikiService.ResolveWikiFilePath(root, "../outside.md").Should().BeNull();
            GitHubWikiService.ResolveWikiFilePath(root, "Missing.md").Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("a.md", true)]
    [InlineData("a.markdown", true)]
    [InlineData("A.MD", true)]
    [InlineData("a.txt", false)]
    [InlineData("a.png", false)]
    public void IsMarkdown_RecognizesExtensions(string path, bool expected)
    {
        GitHubWikiService.IsMarkdown(path).Should().Be(expected);
    }

    [Fact]
    public void ScopePages_NoScope_ReturnsAllSortedDistinct()
    {
        var pages = GitHubWikiService.BuildPagesFromPaths([
            "docs/guide.md", "README.md", "docs/guide.md"
        ]);

        var scoped = GitHubWikiService.ScopePages(pages, Array.Empty<string>());

        scoped.Select(p => p.Path).Should().Equal("docs/guide.md", "README.md");
    }

    [Fact]
    public void ScopePages_WithScopes_NarrowsToConfiguredSubtrees()
    {
        var pages = GitHubWikiService.BuildPagesFromPaths([
            "README.md", "docs/guide.md", "docs/deep/page.md", "src/readme.md"
        ]);

        var scoped = GitHubWikiService.ScopePages(pages, new[] { "docs", "README.md" });

        scoped.Select(p => p.Path).Should().Equal("docs/deep/page.md", "docs/guide.md", "README.md");
    }

    [Theory]
    [InlineData("""{ "tree": [], "truncated": true }""", true)]
    [InlineData("""{ "tree": [], "truncated": false }""", false)]
    [InlineData("""{ "tree": [] }""", false)]
    public void IsTreeTruncated_ReadsGitHubFlag(string json, bool expected)
    {
        GitHubWikiService.IsTreeTruncated(json).Should().Be(expected);
    }

    private static string CreateTempWikiRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExportAzureWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
