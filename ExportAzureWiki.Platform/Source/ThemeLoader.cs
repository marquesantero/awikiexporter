using System.Globalization;
using System.Text.RegularExpressions;

namespace ExportAzureWiki;

public class ThemeLoader
{
    public static List<Theme>? ThemeList;

    /// <summary>
    /// Discovers the highlight.js themes shipped with the app and returns
    /// them as a UI-toolkit-neutral list. The previous WinForms ComboBox-
    /// based overload was removed in Fase 3.1; consumers (WPF, CLI) now
    /// bind to this list directly.
    /// </summary>
    public static List<Theme> GetThemeList()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var pathTheme = Path.Combine(baseDirectory, "style", "styles");
        if (!Directory.Exists(pathTheme))
        {
            return new List<Theme>();
        }

        var themeFiles = Directory.GetFiles(pathTheme, "*.min.css");

        return (from file in themeFiles
            let themeName = ExtractThemeName(file)
            select new Theme { ThemeName = themeName, FilePath = file }).ToList();
    }

    private static string ExtractThemeName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        fileName = fileName.Replace(".min", "");
        var cleanName = Regex.Replace(fileName, @"[^a-zA-Z0-9]", " ");
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName.ToLower());
    }
}

public class Theme
{
    public string? ThemeName { get; set; }
    public string? FilePath { get; set; }
}
