using System.Text.RegularExpressions;
using ExportAzureWiki.Data;

namespace ExportAzureWiki.Platform.Data;

/// <summary>
/// Validates and quotes SQL identifiers (database, table, column names) that
/// come from configuration or any source other than a string literal in code.
///
/// Identifier interpolation is the place SQL injection slips into otherwise
/// parameterized code: <c>$"SELECT * FROM {tableName}"</c> looks safe but
/// hands the attacker the keys when <c>tableName</c> is user-controlled.
/// Parameter placeholders (<c>@p</c>) cannot stand in for identifiers in any
/// major dialect, so the only safe path is allow-list + provider quoting.
///
/// Use this for every identifier whose value is not a compile-time constant.
/// For literal table/column names hard-coded in source there is no value;
/// they cannot be subverted at runtime.
/// </summary>
public static partial class SqlIdentifier
{
    // ANSI-ish maximum: leading letter/underscore, then up to 62 more chars.
    // 63 total fits Postgres (63), SQL Server (128), MySQL (64), SQLite (any).
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();

    /// <summary>
    /// Validates an identifier and returns it quoted for the given dialect.
    /// Throws <see cref="ArgumentException"/> if the identifier fails the
    /// allow-list; the caller should let the exception surface as an
    /// invalid-configuration error rather than swallow it.
    /// </summary>
    public static string Quote(string identifier, DatabaseType databaseType)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        if (!AllowedPattern().IsMatch(identifier))
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' contains characters that are not allowed. " +
                "Use only letters, digits, and underscores; the first character must be a letter or underscore.",
                nameof(identifier));
        }

        return databaseType switch
        {
            DatabaseType.SqlServer => $"[{identifier}]",
            DatabaseType.PostgreSQL => $"\"{identifier}\"",
            DatabaseType.MySQL => $"`{identifier}`",
            DatabaseType.SQLite => $"\"{identifier}\"",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported."),
        };
    }

    /// <summary>
    /// Validates an identifier without quoting. Useful when the value will
    /// be embedded in a parameter (e.g. SQL Server <c>sys.databases.name</c>)
    /// rather than a SQL identifier position, but still needs to be a sane
    /// identifier shape.
    /// </summary>
    public static string Validate(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        if (!AllowedPattern().IsMatch(identifier))
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' contains characters that are not allowed.",
                nameof(identifier));
        }

        return identifier;
    }
}
