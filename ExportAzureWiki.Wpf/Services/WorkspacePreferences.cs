using System.IO;
using System.Text.Json;

namespace ExportAzureWiki.Wpf.Services;

/// <summary>
/// Per-user, per-machine UI preferences for the workspace, persisted as JSON
/// under %LocalAppData%. Kept separate from the shared DB application settings
/// (which are the same across machines) because these are local choices.
/// All operations are best-effort: failures never block the UI.
/// </summary>
public sealed class WorkspacePreferences
{
    public string? LastWikiId { get; set; }
    public string? CodeThemeFilePath { get; set; }
    public bool DarkMode { get; set; }
    public int LastTabIndex { get; set; }
    public bool OfflineExport { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExportAzureWiki",
        "preferences.json");

    public static WorkspacePreferences Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return new WorkspacePreferences();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WorkspacePreferences>(json) ?? new WorkspacePreferences();
        }
        catch
        {
            return new WorkspacePreferences();
        }
    }

    public void Save()
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Preferences are best-effort; never surface a failure to the user.
        }
    }
}
