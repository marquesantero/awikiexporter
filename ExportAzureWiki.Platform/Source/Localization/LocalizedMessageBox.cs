using ExportAzureWiki.Core.Notifications;
using ExportAzureWiki.Platform.Notifications;

namespace ExportAzureWiki.Localization;

/// <summary>
/// Thin facade over <see cref="UserNotifier.Active"/> that automatically
/// runs each string through <see cref="LocalizationManager"/>. Kept under
/// the legacy name so the existing call sites do not have to be rewritten
/// while the dependency on System.Windows.Forms is removed.
/// </summary>
public static class LocalizedMessageBox
{
    public static void ShowInfo(string text, string caption)
        => UserNotifier.Active.Info(Localize(text), Localize(caption));

    public static void ShowWarning(string text, string caption)
        => UserNotifier.Active.Warn(Localize(text), Localize(caption));

    public static void ShowError(string text, string caption)
        => UserNotifier.Active.Error(Localize(text), Localize(caption));

    public static ConfirmResult ShowConfirm(string text, string caption, bool allowCancel = false)
        => UserNotifier.Active.Confirm(Localize(text), Localize(caption), allowCancel);

    private static string Localize(string key)
        => LocalizationManager.S(key, key);
}
