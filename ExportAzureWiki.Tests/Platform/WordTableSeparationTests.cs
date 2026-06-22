using DocumentFormat.OpenXml.Wordprocessing;
using ExportAzureWiki;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Two tables that are direct siblings get merged by Word on render, which
/// corrupts column layout (a code-block table followed by a data table made
/// every row wrap to a full page). SeparateAdjacentTables must guarantee no
/// two tables remain adjacent in the body.
/// </summary>
public sealed class WordTableSeparationTests
{
    private static Table TwoColTable(string a, string b)
        => new(new TableRow(
            new TableCell(new Paragraph(new Run(new Text(a)))),
            new TableCell(new Paragraph(new Run(new Text(b))))));

    [Fact]
    public void SeparateAdjacentTables_InsertsParagraphBetweenAdjacentTables()
    {
        var body = new Body(
            TwoColTable("a", "b"),   // adjacent to the next table
            TwoColTable("c", "d"));

        ExportService.SeparateAdjacentTables(body);

        var kinds = body.ChildElements.Select(e => e.LocalName).ToList();
        kinds.Should().Equal("tbl", "p", "tbl");
    }

    [Fact]
    public void SeparateAdjacentTables_SeparatesChainOfThreeTables()
    {
        var body = new Body(
            TwoColTable("a", "b"),
            TwoColTable("c", "d"),
            TwoColTable("e", "f"));

        ExportService.SeparateAdjacentTables(body);

        body.ChildElements.Select(e => e.LocalName)
            .Should().Equal("tbl", "p", "tbl", "p", "tbl");
        AssertNoAdjacentTables(body);
    }

    [Fact]
    public void SeparateAdjacentTables_LeavesAlreadySeparatedTablesUntouched()
    {
        var body = new Body(
            TwoColTable("a", "b"),
            new Paragraph(new Run(new Text("between"))),
            TwoColTable("c", "d"));

        ExportService.SeparateAdjacentTables(body);

        body.Elements<Paragraph>().Count().Should().Be(1);
        AssertNoAdjacentTables(body);
    }

    private static void AssertNoAdjacentTables(Body body)
    {
        foreach (var table in body.Elements<Table>())
        {
            (table.NextSibling() is Table).Should().BeFalse("no two tables may stay adjacent");
        }
    }
}
