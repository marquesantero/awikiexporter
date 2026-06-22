namespace ExportAzureWiki.Core.Notifications;

/// <summary>
/// Drops every notification. Used as the bootstrap default before the
/// hosting app (WPF / CLI / test) installs its real adapter, and as the
/// noisy-things-disabled implementation in headless scenarios.
/// </summary>
public sealed class NullUserNotifier : IUserNotifier
{
    public static IUserNotifier Instance { get; } = new NullUserNotifier();

    private NullUserNotifier() { }

    public void Info(string message, string title) { }
    public void Warn(string message, string title) { }
    public void Error(string message, string title) { }
    public ConfirmResult Confirm(string message, string title, bool allowCancel = false) => ConfirmResult.No;
}
