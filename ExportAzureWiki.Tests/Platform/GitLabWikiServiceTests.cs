using ExportAzureWiki.Services.WikiProviders;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Covers the pure URL/JSON helpers of the GitLab Markdown source. Network
/// calls aren't exercised here (no live token/project); the parsing and URL
/// shapes are where regressions would silently break fetching.
/// </summary>
public sealed class GitLabWikiServiceTests
{
    // A page of the Repository Tree API (recursive). Mixes blobs and trees.
    private const string TreeJson = """
    [
      { "id": "s1", "name": "README.md",     "type": "blob", "path": "README.md" },
      { "id": "s2", "name": "guide.md",      "type": "blob", "path": "docs/guide.md" },
      { "id": "s3", "name": "deep.markdown", "type": "blob", "path": "docs/sub/deep.markdown" },
      { "id": "s4", "name": "image.png",     "type": "blob", "path": "docs/image.png" },
      { "id": "s5", "name": "docs",          "type": "tree", "path": "docs" },
      { "id": "s6", "name": "code.cs",       "type": "blob", "path": "src/code.cs" }
    ]
    """;

    [Fact]
    public void ParseTreePages_ReturnsAllMarkdownBlobs()
    {
        var pages = GitLabWikiService.ParseTreePages(TreeJson);

        pages.Select(p => p.Path).Should().BeEquivalentTo(
            new[] { "README.md", "docs/guide.md", "docs/sub/deep.markdown" });
    }

    [Fact]
    public void ParseTreePages_IgnoresTreesAndNonMarkdown()
    {
        var pages = GitLabWikiService.ParseTreePages(TreeJson);

        pages.Should().NotContain(p => p.Path == "docs");          // tree
        pages.Should().NotContain(p => p.Path == "docs/image.png"); // not md
        pages.Should().NotContain(p => p.Path == "src/code.cs");
    }

    [Fact]
    public void ParseTreePages_DerivesTitleFromFileName()
    {
        var pages = GitLabWikiService.ParseTreePages(TreeJson);

        pages.Single(p => p.Path == "docs/guide.md").Title.Should().Be("guide");
        pages.Single(p => p.Path == "docs/sub/deep.markdown").Title.Should().Be("deep");
    }

    [Fact]
    public void ParseTreePages_EmptyArray_ReturnsEmpty()
    {
        GitLabWikiService.ParseTreePages("[]").Should().BeEmpty();
        GitLabWikiService.ParseTreePages("{}").Should().BeEmpty();
    }

    [Fact]
    public void ScopePages_WithDocsScope_FiltersToThatSubtree()
    {
        var pages = GitLabWikiService.ParseTreePages(TreeJson);

        var scoped = GitLabWikiService.ScopePages(pages, new[] { "docs" });

        scoped.Select(p => p.Path).Should().Equal("docs/guide.md", "docs/sub/deep.markdown");
        scoped.Should().NotContain(p => p.Path == "README.md");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("docs", "docs")]
    [InlineData("/docs/", "docs")]
    [InlineData("  docs/guide  ", "docs/guide")]
    public void NormalizeDocsPath_TrimsSlashesAndSpace(string input, string expected)
    {
        GitLabWikiService.NormalizeDocsPath(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "Repo")]
    [InlineData("", "Repo")]
    [InlineData("repo", "Repo")]
    [InlineData("Wiki", "Wiki")]
    [InlineData(" wiki ", "Wiki")]
    public void NormalizeMode_DefaultsToRepoAndAcceptsWiki(string? input, string expected)
    {
        GitLabWikiService.NormalizeMode(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "https://gitlab.com")]
    [InlineData("", "https://gitlab.com")]
    [InlineData("https://gitlab.example.com", "https://gitlab.example.com")]
    [InlineData("https://gitlab.example.com/", "https://gitlab.example.com")]
    [InlineData(" https://gitlab.example.com/ ", "https://gitlab.example.com")]
    public void NormalizeApiBase_TrimsAndDefaults(string? input, string expected)
    {
        GitLabWikiService.NormalizeApiBase(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("123", "123")]                          // numeric id kept as-is
    [InlineData(" 123 ", "123")]
    [InlineData("group/project", "group%2Fproject")]     // path is URL-encoded
    [InlineData("group/sub/project", "group%2Fsub%2Fproject")]
    [InlineData("/group/project/", "group%2Fproject")]
    public void EncodeProjectId_NumericVsPath(string input, string expected)
    {
        GitLabWikiService.EncodeProjectId(input).Should().Be(expected);
    }

    [Fact]
    public void BuildProjectUrl_TargetsProjectsApi()
    {
        GitLabWikiService.BuildProjectUrl("https://gitlab.com", "group/project")
            .Should().Be("https://gitlab.com/api/v4/projects/group%2Fproject");
    }

    [Fact]
    public void BuildTreeUrl_UsesRecursivePagedTreeApi()
    {
        GitLabWikiService.BuildTreeUrl("https://gitlab.com", "123", "main", page: 2, perPage: 100)
            .Should().Be("https://gitlab.com/api/v4/projects/123/repository/tree?ref=main&recursive=true&per_page=100&page=2");
    }

    [Fact]
    public void BuildRawUrl_EncodesFilePathAndTargetsRawEndpoint()
    {
        GitLabWikiService.BuildRawUrl("https://gitlab.com", "123", "main", "docs/guide.md")
            .Should().Be("https://gitlab.com/api/v4/projects/123/repository/files/docs%2Fguide.md/raw?ref=main");
    }

    [Fact]
    public void BuildRawUrl_NormalizesLeadingSlash()
    {
        GitLabWikiService.BuildRawUrl("https://gitlab.com", "123", "feature/x", "/docs/guide.md")
            .Should().Be("https://gitlab.com/api/v4/projects/123/repository/files/docs%2Fguide.md/raw?ref=feature%2Fx");
    }

    [Fact]
    public void BuildWikiUrls_TargetWikisApi()
    {
        GitLabWikiService.BuildWikiListUrl("https://gitlab.com", "123")
            .Should().Be("https://gitlab.com/api/v4/projects/123/wikis");
        GitLabWikiService.BuildWikiPageUrl("https://gitlab.com", "123", "home")
            .Should().Be("https://gitlab.com/api/v4/projects/123/wikis/home");
    }

    [Fact]
    public void ParseWikiList_ReturnsSlugAndTitleSorted()
    {
        const string json = """
        [
          { "format": "markdown", "slug": "home",        "title": "Home" },
          { "format": "markdown", "slug": "api/Auth",     "title": "Auth" },
          { "format": "markdown", "slug": "no-title" }
        ]
        """;

        var pages = GitLabWikiService.ParseWikiList(json);

        pages.Select(p => p.Path).Should().Equal("api/Auth", "home", "no-title");
        pages.Single(p => p.Path == "home").Title.Should().Be("Home");
        // Missing title falls back to the slug's file name.
        pages.Single(p => p.Path == "no-title").Title.Should().Be("no-title");
    }

    [Fact]
    public void ParseWikiPageContent_ReadsContentAndFormat()
    {
        const string json = """
        { "content": "# Hello", "format": "markdown", "slug": "home", "title": "Home" }
        """;

        var content = GitLabWikiService.ParseWikiPageContent(json, "home");

        content.PageId.Should().Be("home");
        content.Content.Should().Be("# Hello");
        content.ContentType.Should().Be("markdown");
    }

    [Fact]
    public void ParseConfiguredStartPoints_SplitsAndNormalizesRootPath()
    {
        var points = GitLabWikiService.ParseConfiguredStartPoints(" /README.md | docs/guide.md | docs/ ");

        points.Should().Equal("docs", "docs/guide.md", "README.md");
    }

    [Fact]
    public void BuildPagesFromPaths_CreatesPagesForConfiguredMarkdownFilesOnly()
    {
        var pages = GitLabWikiService.BuildPagesFromPaths(["/README.md", "docs/guide.md", "assets/logo.png"]);

        pages.Select(p => p.Path).Should().Equal("docs/guide.md", "README.md");
        pages.Single(p => p.Path == "README.md").Title.Should().Be("README");
    }

    [Theory]
    [InlineData("a.md", true)]
    [InlineData("a.markdown", true)]
    [InlineData("A.MD", true)]
    [InlineData("a.txt", false)]
    [InlineData("a.png", false)]
    public void IsMarkdown_RecognizesExtensions(string path, bool expected)
    {
        GitLabWikiService.IsMarkdown(path).Should().Be(expected);
    }

    [Fact]
    public void ParseDefaultBranch_ReadsProjectDefaultBranch()
    {
        GitLabWikiService.ParseDefaultBranch("""{ "id": 1, "default_branch": "master" }""")
            .Should().Be("master");
        GitLabWikiService.ParseDefaultBranch("""{ "id": 1, "default_branch": "main" }""")
            .Should().Be("main");
    }

    [Fact]
    public void ParseDefaultBranch_MissingOrInvalid_ReturnsEmpty()
    {
        GitLabWikiService.ParseDefaultBranch("{}").Should().BeEmpty();
        GitLabWikiService.ParseDefaultBranch("[]").Should().BeEmpty();
    }

    [Fact]
    public void DefaultBranchFallback_IsMaster()
    {
        // GitLab's broad install base (and self-managed) default to master, so a
        // blind fallback of master is safer than main when the project can't be
        // queried and Branch is left empty.
        GitLabWikiService.DefaultBranchFallback.Should().Be("master");
    }
}
