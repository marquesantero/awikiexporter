using System.Globalization;

namespace ExportAzureWiki.Core.Localization;

public static class AppText
{
    private static Func<string, string?, string> _translate = static (key, fallback) => fallback ?? key;
    private static Func<string, object[], string> _format = static (template, args) => string.Format(CultureInfo.CurrentCulture, template, args);

    public static void Configure(
        Func<string, string?, string> translate,
        Func<string, object[], string>? format = null)
    {
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
        if (format != null)
        {
            _format = format;
        }
    }

    public static string S(string key, string? fallback = null)
    {
        return _translate(key, fallback);
    }

    public static string Sf(string key, params object[] args)
    {
        var template = S(key, key);
        return _format(template, args);
    }
}
