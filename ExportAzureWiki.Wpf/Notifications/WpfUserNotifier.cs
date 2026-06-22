using System.Windows;
using ExportAzureWiki.Core.Notifications;

namespace ExportAzureWiki.Wpf.Notifications;

/// <summary>
/// Routes Platform notifications to <see cref="MessageBox"/>. The shell
/// installs this adapter in App.OnStartup so the rest of Platform stays
/// free of any WPF dependency.
/// </summary>
public sealed class WpfUserNotifier : IUserNotifier
{
    public void Info(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warn(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Error(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public ConfirmResult Confirm(string message, string title, bool allowCancel = false)
    {
        var buttons = allowCancel ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo;
        var result = MessageBox.Show(message, title, buttons, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => ConfirmResult.Yes,
            MessageBoxResult.Cancel => ConfirmResult.Cancel,
            _ => ConfirmResult.No,
        };
    }
}
