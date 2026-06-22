using ExportAzureWiki.Localization;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class LanguageOption
{
    public LanguageOption(SupportedLanguage value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public SupportedLanguage Value { get; }

    public string DisplayName { get; }
}

