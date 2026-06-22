using ExportAzureWiki.Services;

namespace ExportAzureWiki.Tests.Security;

public sealed class HtmlSanitizerTests
{
    [Fact]
    public void Removes_Script_Tag()
    {
        var input = "<p>safe</p><script>alert(1)</script>";
        HtmlSanitizer.Sanitize(input).Should().NotContain("<script", "scripts are stripped wholesale");
    }

    [Fact]
    public void Removes_Iframe_And_Object_And_Embed()
    {
        var input = "<iframe src='x'></iframe><object data='x'></object><embed src='x'>";
        var output = HtmlSanitizer.Sanitize(input);
        output.Should().NotContain("<iframe", "iframes are stripped");
        output.Should().NotContain("<object", "objects are stripped");
        output.Should().NotContain("<embed", "embeds are stripped");
    }

    [Fact]
    public void Removes_Event_Handler_Attributes()
    {
        var input = "<img src=\"x\" onerror=\"fetch('//attacker')\" onclick=\"alert(1)\">";
        var output = HtmlSanitizer.Sanitize(input);
        output.Should().NotContain("onerror");
        output.Should().NotContain("onclick");
        output.Should().Contain("src=\"x\"", "the src attribute is benign and stays");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("vbscript:msgbox('x')")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void Strips_Dangerous_Url_Schemes_From_Href(string url)
    {
        var input = $"<a href=\"{url}\">click</a>";
        HtmlSanitizer.Sanitize(input).Should().NotContain(url);
    }

    [Fact]
    public void Keeps_Safe_Href()
    {
        var input = "<a href=\"https://example.com/safe\">click</a>";
        HtmlSanitizer.Sanitize(input).Should().Contain("https://example.com/safe");
    }

    [Fact]
    public void Drops_Style_With_Expression()
    {
        var input = "<div style=\"width: expression(alert(1))\">x</div>";
        HtmlSanitizer.Sanitize(input).Should().NotContain("expression(");
    }

    [Fact]
    public void Keeps_Plain_Style_Attribute()
    {
        var input = "<div style=\"color: red\">x</div>";
        HtmlSanitizer.Sanitize(input).Should().Contain("color: red");
    }

    [Fact]
    public void Drops_Srcdoc_Attribute()
    {
        var input = "<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>";
        // The iframe itself is stripped, but verify srcdoc never survives
        // even if a future change relaxes iframe.
        HtmlSanitizer.Sanitize(input).Should().NotContain("srcdoc");
    }

    [Fact]
    public void Preserves_Plain_Wiki_Content()
    {
        var input = "<h1>Title</h1><p>Some <strong>bold</strong> text.</p><ul><li>a</li><li>b</li></ul>";
        HtmlSanitizer.Sanitize(input).Should().Contain("<strong>bold</strong>").And.Contain("<li>a</li>");
    }
}
