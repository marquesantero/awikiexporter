using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Platform.Setup;

namespace ExportAzureWiki.Wpf.Services;

/// <summary>
/// UI orchestration for "clear all local data": confirm with the user, wipe
/// the local state (registry + data folders) via
/// <see cref="LocalDataResetService"/>, then shut the app down so the deferred
/// folder deletion can run and the next launch starts at onboarding.
/// </summary>
public static class AppReset
{
    public static void PromptAndReset(Window? owner)
    {
        var title = AppText.S("wpf.reset.confirm.title", "Reset all data");
        var message = AppText.S(
            "wpf.reset.confirm.message",
            "This permanently clears all local configuration on this machine (local database, admin account, connections, caches and saved credentials) and closes the app. External databases are not affected. Continue?");

        var confirm = owner != null
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        LocalDataResetService.ResetAll();

        var done = AppText.S(
            "wpf.reset.done",
            "Local data was cleared. The application will now close; reopen it to run setup again.");
        if (owner != null)
        {
            MessageBox.Show(owner, done, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(done, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        Application.Current.Shutdown();
    }
}
