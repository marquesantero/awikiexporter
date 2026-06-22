using ExportAzureWiki.Data;
using ExportAzureWiki.Platform.Data;

namespace ExportAzureWiki.Tests.Security;

public sealed class SqlIdentifierTests
{
    [Theory]
    [InlineData("users")]
    [InlineData("Users")]
    [InlineData("_internal")]
    [InlineData("table123")]
    [InlineData("a")]
    public void Validate_Accepts_Plain_Identifiers(string identifier)
    {
        SqlIdentifier.Validate(identifier).Should().Be(identifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1users")]              // leading digit
    [InlineData("user-table")]          // hyphen
    [InlineData("user table")]          // space
    [InlineData("user;DROP")]           // semicolon
    [InlineData("user\"name")]          // quote
    [InlineData("user`name")]           // backtick
    [InlineData("xx\";DROP TABLE x--")] // canonical injection payload
    [InlineData("[users]")]             // pre-quoted
    public void Validate_Rejects_Hostile_Or_Empty(string identifier)
    {
        var act = () => SqlIdentifier.Validate(identifier);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_Rejects_Identifier_Over_63_Chars()
    {
        var tooLong = new string('a', 64);
        var act = () => SqlIdentifier.Validate(tooLong);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(DatabaseType.SqlServer, "users", "[users]")]
    [InlineData(DatabaseType.PostgreSQL, "users", "\"users\"")]
    [InlineData(DatabaseType.MySQL, "users", "`users`")]
    [InlineData(DatabaseType.SQLite, "users", "\"users\"")]
    public void Quote_Wraps_With_Dialect_Specific_Delimiters(DatabaseType dialect, string raw, string expected)
    {
        SqlIdentifier.Quote(raw, dialect).Should().Be(expected);
    }

    [Fact]
    public void Quote_Rejects_Hostile_Identifier_Before_Reaching_The_Database()
    {
        var act = () => SqlIdentifier.Quote("xx\";DROP TABLE x--", DatabaseType.SqlServer);
        act.Should().Throw<ArgumentException>();
    }
}
