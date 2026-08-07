namespace ExportAzureWiki.Core.Notifications;

/// <summary>
/// Tristate result of <see cref="IUserNotifier.Confirm"/>. Cancel is only
/// reachable when the caller opts into a three-button dialog by passing
/// <c>allowCancel: true</c>.
/// </summary>
public enum ConfirmResult
{
    Yes,
    No,
    Cancel
}

/// <summary>
/// Surface for one-way notifications and yes/no confirmations.
///
/// The Platform depends on this interface so it stays free of any WPF /
/// WinForms types: the WPF shell binds it to <c>System.Windows.MessageBox</c>,
/// the CLI binds it to <c>Console</c>, and tests bind it to a recording
/// double. Code in Platform reaches the active notifier through
/// <c>UserNotifier.Active</c>.
/// </summary>
public interface IUserNotifier
{
    void Info(string message, string title);
    void Warn(string message, string title);
    void ShowError(string message, string title);
    ConfirmResult Confirm(string message, string title, bool allowCancel = false);
}
