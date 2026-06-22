using System.Diagnostics;
using Microsoft.Win32;
using Serilog;

namespace ExportAzureWiki.Platform.Setup;

/// <summary>
/// Wipes this Windows user's local ExportAzureWiki state so the next launch
/// starts at onboarding. MSIX has no uninstall hook and the full-trust app
/// stores data in the real profile, so this is the supported "clean slate".
///
/// The onboarding gate lives in the registry (<see cref="SetupChecker"/>), so
/// the registry key is removed in-process immediately. The data folders hold
/// the live SQLite DB and DPAPI key, which are locked while the app runs, so
/// their deletion is deferred to a detached process that waits for this
/// process to exit. External databases are never touched.
/// </summary>
public static class LocalDataResetService
{
    private const string RegistrySubKey = @"Software\ExportAzureWiki";

    private static string LocalDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExportAzureWiki");

    private static string TempDir => Path.Combine(Path.GetTempPath(), "ExportAzureWiki");

    /// <summary>
    /// Removes the onboarding/registry state now and schedules deletion of the
    /// data folders after the app exits. Call this immediately before shutting
    /// the application down. Returns false if the registry could not be cleared
    /// (in which case onboarding would not re-trigger).
    /// </summary>
    public static bool ResetAll()
    {
        var registryCleared = RemoveRegistry();
        ScheduleFolderDeletionAfterExit(LocalDataDir, TempDir);
        return registryCleared;
    }

    private static bool RemoveRegistry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RegistrySubKey, throwOnMissingSubKey: false);
            Log.Information("Local data reset: registry key {Key} removed", RegistrySubKey);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Local data reset: failed to remove registry key {Key}", RegistrySubKey);
            return false;
        }
    }

    private static void ScheduleFolderDeletionAfterExit(params string[] dirs)
    {
        try
        {
            var pid = Environment.ProcessId;
            var removals = string.Join(
                " ",
                dirs.Select(d => $"Remove-Item -LiteralPath '{d.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue;"));

            // Wait for THIS process to release the DB/key files, then delete.
            var script =
                $"try {{ Wait-Process -Id {pid} -Timeout 30 -ErrorAction SilentlyContinue }} catch {{}}; {removals}";

            var psi = new ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-WindowStyle");
            psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            Process.Start(psi);
            Log.Information("Local data reset: scheduled deletion of {Dirs} after exit", string.Join(", ", dirs));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Local data reset: failed to schedule folder deletion");
        }
    }
}
