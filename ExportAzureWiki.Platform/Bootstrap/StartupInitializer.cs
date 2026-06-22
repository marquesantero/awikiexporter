using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Localization;
using Dapper;

namespace ExportAzureWiki.Platform.Bootstrap;

public static class StartupInitializer
{
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        // Keep parity with WinForms startup: map snake_case DB columns to PascalCase properties.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        AppText.Configure(
            (key, fallback) => LocalizationManager.S(key, fallback ?? key),
            (template, args) => string.Format(template, args));
    }
}







