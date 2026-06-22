using ExportAzureWiki.Localization;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Guards the hand-maintained PT/EN dictionaries against drift. A key present
/// in only one language makes the UI fall back to the wrong language (S(key)
/// returns Portuguese when an English key is missing), which is exactly the
/// class of bug this test prevents.
/// </summary>
public sealed class LocalizationParityTests
{
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
