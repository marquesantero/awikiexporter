using ExportAzureWiki;
using Markdig;

namespace ExportAzureWiki.Tests.Platform;

public sealed class MarkdownDialectTests
{
    [Fact]
    public void GitHubDialect_AddsBlankLineBeforeValidGfmTableWithoutOuterPipes()
    {
        var markdown = """
        Intro paragraph
        Command | Description
        --- | ---
        git status | List files
        """;

        var normalized = HtmlContentGenerator.NormalizeMarkdownForDialect(markdown, WikiMarkdownDialect.GitHub);

        normalized.Should().Contain("Intro paragraph\n\nCommand | Description");
    }

    [Fact]
    public void GitHubDialect_DoesNotTreatInvalidDelimiterAsTable()
    {
        var markdown = """
        Intro paragraph
        Command | Description
        -- | --
        git status | List files
        """;

        var normalized = HtmlContentGenerator.NormalizeMarkdownForDialect(markdown, WikiMarkdownDialect.GitHub);

        normalized.Should().Contain("Intro paragraph\nCommand | Description");
        normalized.Should().NotContain("Intro paragraph\n\nCommand | Description");
    }

    [Fact]
    public void GitHubDialect_DoesNotModifyPipeExamplesInsideCodeFence()
    {
        var markdown = """
        ```markdown
        Intro paragraph
        Command | Description
        --- | ---
        git status | List files
        ```
        """;

        var normalized = HtmlContentGenerator.NormalizeMarkdownForDialect(markdown, WikiMarkdownDialect.GitHub);

        normalized.Should().Be(markdown);
    }

    [Fact]
    public void GitHubDialect_RendersTableAfterArchitectureCodeFence()
    {
        var markdown = """
        ## Architecture

        ```text
        ExportAzureWiki.Core
          Models and service contracts

        ExportAzureWiki.Platform
          Data access, wiki providers, auth, rendering, export engines
        ```

        | Project | Responsibility |
        | --- | --- |
        | `ExportAzureWiki.Core/` | Shared models, application service contracts, and UI-independent rules. |
        | `ExportAzureWiki.Platform/` | Infrastructure, wiki providers, persistence, authentication, authorization, rendering, and export engines. |
        """;

        var normalized = HtmlContentGenerator.NormalizeMarkdownForDialect(markdown, WikiMarkdownDialect.GitHub);
        var html = Markdown.ToHtml(normalized, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        html.Should().Contain("<table>");
        html.Should().Contain("<th>Project</th>");
        html.Should().Contain("<td><code>ExportAzureWiki.Core/</code></td>");
    }

    [Fact]
    public void AzureDialect_ConvertsAzureMermaidContainerToFencedMermaid()
    {
        var markdown = """
        ::: mermaid
        graph TD;
            A-->B;
        :::
        """;

        var normalized = HtmlContentGenerator.NormalizeMarkdownForDialect(markdown, WikiMarkdownDialect.AzureDevOps);

        normalized.Should().StartWith("```mermaid");
        normalized.Should().Contain("graph TD;");
        normalized.Should().EndWith("```");
    }
}
