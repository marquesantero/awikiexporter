namespace ExportAzureWiki;

/// <summary>
/// Holds the currently selected highlight.js theme and the dark-mode flag
/// used by the renderer. The fields are static because the renderer and
/// the export pipeline both read them ambient-style. Construction is now
/// UI-stack-neutral: the WPF shell uses the static fields directly, while
/// CLI/tests can instantiate the class without a UI toolkit dependency.
/// </summary>
public class ThemeManager
{
    public static bool DarkModeCheckBox;
    public static string? SelectedTheme;

    public ThemeManager()
    {
    }

    public void LoadThemes()
    {
        ThemeLoader.ThemeList = ThemeLoader.GetThemeList();
    }

    public string? GetCurrentTheme() => GetCurrentThemeStatic();

    public static string? GetCurrentThemeStatic()
    {
        if (!string.IsNullOrEmpty(SelectedTheme))
            return SelectedTheme;

        if (ThemeLoader.ThemeList == null || ThemeLoader.ThemeList.Count == 0)
            return null;

        return ThemeLoader.ThemeList
            .FirstOrDefault(t => t.ThemeName!.Equals("Default", StringComparison.OrdinalIgnoreCase))
            ?.FilePath;
    }

    public bool IsDarkMode() => DarkModeCheckBox;
}
