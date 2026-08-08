using ExportAzureWiki.Localization;
using System.Globalization;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Guards the hand-maintained PT/EN dictionaries against drift. A key present
/// in only one language makes the UI fall back to the wrong language (S(key)
/// returns Portuguese when an English key is missing), which is exactly the
/// class of bug this test prevents.
/// </summary>
public sealed class LocalizationParityTests
{
    [Theory]
    [InlineData("pt-BR", SupportedLanguage.Portuguese)]
    [InlineData("pt-PT", SupportedLanguage.Portuguese)]
    [InlineData("pt", SupportedLanguage.Portuguese)]
    [InlineData("en-US", SupportedLanguage.English)]
    [InlineData("es-ES", SupportedLanguage.English)]
    [InlineData("fr-FR", SupportedLanguage.English)]
    public void DefaultLanguageFollowsSystemCulture(string cultureName, SupportedLanguage expected)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        var actual = LocalizationManager.DetermineDefaultLanguage(culture);

        actual.Should().Be(expected);
    }

    [Fact]
    public void EveryPortugueseKeyHasAnEnglishCounterpart()
    {
        var pt = LocalizationManager.SemanticPtKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var en = LocalizationManager.SemanticEnKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingInEn = pt.Except(en).OrderBy(k => k).ToList();

        missingInEn.Should().BeEmpty(
            "every PT key must exist in EN; missing: " + string.Join(", ", missingInEn));
    }

    [Fact]
    public void EveryEnglishKeyHasAPortugueseCounterpart()
    {
        var pt = LocalizationManager.SemanticPtKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var en = LocalizationManager.SemanticEnKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingInPt = en.Except(pt).OrderBy(k => k).ToList();

        missingInPt.Should().BeEmpty(
            "every EN key must exist in PT; missing: " + string.Join(", ", missingInPt));
    }
}
