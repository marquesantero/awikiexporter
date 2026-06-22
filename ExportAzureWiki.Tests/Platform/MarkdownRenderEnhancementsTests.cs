using ExportAzureWiki;
using HtmlAgilityPack;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Covers the Markdown rendering features added so that content commonly found
/// in real .md files is not lost: YAML front matter, GitHub alert callouts,
/// emoji, and the Word-side handling of alerts, task checkboxes and details.
/// </summary>
public sealed class MarkdownRenderEnhancementsTests
{
    // ---- md -> HTML (Markdig pipeline) --------------------------------------

    // ---- Math delimiters ----------------------------------------------------
    // Inline math must reach MathJax as <span class="math">\(...\)</span>.
    // Markdig's escape-processing strips the backslash from author-written \( \)
    // / \[ \] delimiters, so the normalizer rewrites them to $/$$ which Markdig's
    // math extension renders. (Regression: inline $...$ was also broken because
    // it was being pre-rewritten to \(...\) and then stripped.)

    [Fact]
    public void InlineMath_BackslashParenDelimiters_RenderAsMath()
    {
        var html = HtmlContentGenerator.RenderMarkdownFragmentWithMathNormalize(
            @"Inline \(U_i\) and \(\delta\) here.");

        html.Should().Contain(@"<span class=""math"">\(U_i\)</span>");
        html.Should().Contain(@"<span class=""math"">\(\delta\)</span>");
        html.Should().NotContain("(\\delta)", "the literal delimiters must not leak through as text");
    }

    [Fact]
    public void InlineMath_DollarDelimiters_RenderAsMath()
    {
        var html = HtmlContentGenerator.RenderMarkdownFragmentWithMathNormalize(
            @"Inline $U_i$ and $\delta$ here.");

        html.Should().Contain(@"<span class=""math"">\(U_i\)</span>");
        html.Should().Contain(@"<span class=""math"">\(\delta\)</span>");
    }

    [Fact]
    public void DisplayMath_BackslashBracketDelimiters_RenderAsMath()
    {
        var html = HtmlContentGenerator.RenderMarkdownFragmentWithMathNormalize(@"\[C = U_i\]");

        html.Should().Contain(@"class=""math""");
        html.Should().Contain(@"\(C = U_i\)");
    }

    [Fact]
    public void MathDelimiters_InsideInlineCode_AreNotConverted()
    {
        var html = HtmlContentGenerator.RenderMarkdownFragmentWithMathNormalize(@"Use `\(x\)` literally.");

        html.Should().Contain(@"<code>\(x\)</code>");
        html.Should().NotContain(@"class=""math""");
    }

    [Fact]
    public void FrontMatter_IsNotRenderedAsContent()
    {
        var markdown = "---\ntitle: My Page\nauthor: Jane\n---\n\n# Heading\n\nBody.";

        var html = HtmlContentGenerator.RenderMarkdownFragment(markdown);

        html.Should().NotContain("title: My Page");
        html.Should().NotContain("author: Jane");
        html.Should().Contain("<h1");
        html.Should().Contain("Body.");
    }

    [Theory]
    [InlineData("NOTE", "markdown-alert-note")]
    [InlineData("TIP", "markdown-alert-tip")]
    [InlineData("WARNING", "markdown-alert-warning")]
    [InlineData("IMPORTANT", "markdown-alert-important")]
    [InlineData("CAUTION", "markdown-alert-caution")]
    public void GitHubAlerts_RenderAsAlertDivs(string kind, string expectedClass)
    {
        var markdown = $"> [!{kind}]\n> Pay attention here.";

        var html = HtmlContentGenerator.RenderMarkdownFragment(markdown);

        html.Should().Contain("markdown-alert");
        html.Should().Contain(expectedClass);
        html.Should().Contain("Pay attention here.");
        html.Should().NotContain($"[!{kind}]", "the alert marker must be consumed, not shown literally");
    }

    [Fact]
    public void Emoji_ShortcodesBecomeUnicode()
    {
        var html = HtmlContentGenerator.RenderMarkdownFragment("Ship it :rocket:");

        html.Should().Contain("\U0001F680"); // 🚀
        html.Should().NotContain(":rocket:");
    }

    // ---- md -> Word (HTML preprocessing) ------------------------------------

    [Fact]
    public void Word_AlertDiv_BecomesBlockquoteWithColoredTitle()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(
            "<div class=\"markdown-alert markdown-alert-warning\">" +
            "<p class=\"markdown-alert-title\">Warning</p><p>Careful.</p></div>");

        ExportService.TransformGitHubFlavoredHtmlForWord(doc);

        var html = doc.DocumentNode.OuterHtml;
        html.Should().Contain("<blockquote");
        html.Should().Contain("color:#9a6700"); // warning color, inline
        html.Should().Contain("WARNING");
        html.Should().Contain("Careful.");
        html.Should().NotContain("markdown-alert", "the class-based styling is dropped for Word");
    }

    [Fact]
    public void Word_TaskListCheckboxes_BecomeGlyphs()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(
            "<ul><li><input type=\"checkbox\" checked=\"checked\" disabled> done</li>" +
            "<li><input type=\"checkbox\" disabled> todo</li></ul>");

        ExportService.TransformGitHubFlavoredHtmlForWord(doc);

        var html = doc.DocumentNode.OuterHtml;
        html.Should().Contain("☑"); // ☑ checked
        html.Should().Contain("☐"); // ☐ unchecked
        html.Should().NotContain("<input");
    }

    [Fact]
    public void Word_Details_AreFlattened_PreservingContent()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<details><summary>More info</summary><p>Hidden content.</p></details>");

        ExportService.TransformGitHubFlavoredHtmlForWord(doc);

        var html = doc.DocumentNode.OuterHtml;
        html.Should().NotContain("<details");
        html.Should().NotContain("<summary");
        html.Should().Contain("More info");
        html.Should().Contain("Hidden content.");
    }

    // ---- Image conversion ---------------------------------------------------

    [Fact]
    public void Word_MermaidBlock_NeverUsesMermaidInk()
    {
        // Diagrams are rendered locally before export; the Word preprocessing
        // must not emit any mermaid.ink URL (privacy/offline). It keeps the
        // source as a code block when no pre-rendered image is present.
        const string html =
            "<html><body><div class=\"mermaid\">graph LR\n  A --&gt; B</div></body></html>";

        var result = ExportService.PreprocessHtmlForWord(html);

        result.Should().NotContain("mermaid.ink");
        result.Should().Contain("graph LR");
    }

    [Fact]
    public void RasterImageConverter_SvgToPng_ProducesPngBytes()
    {
        const string svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\">" +
            "<rect width=\"24\" height=\"24\" fill=\"#0969da\"/></svg>";

        var png = RasterImageConverter.SvgToPng(svg);

        png.Should().NotBeNull();
        png!.Length.Should().BeGreaterThan(8);
        // PNG magic number.
        png[0].Should().Be(0x89);
        png[1].Should().Be((byte)'P');
        png[2].Should().Be((byte)'N');
        png[3].Should().Be((byte)'G');
    }

    [Fact]
    public void RasterImageConverter_DetectsWebpAndSvgMagic()
    {
        var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        RasterImageConverter.LooksLikeWebp(webp).Should().BeTrue();

        var svgBytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        RasterImageConverter.LooksLikeSvg(svgBytes).Should().BeTrue();

        var notWebp = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0, 0, 0, 0, 0, 0, 0 };
        RasterImageConverter.LooksLikeWebp(notWebp).Should().BeFalse();
    }
}
