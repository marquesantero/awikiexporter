using ExportAzureWiki.Core.Notifications;

namespace ExportAzureWiki.Platform.Notifications;

/// <summary>
/// Routes notifications to <see cref="Console"/>. Warn/Error go to stderr
/// so they can be captured separately in CI logs. Confirm reads a single
/// line from stdin (Y/N, and optionally C for cancel).
///
/// CLI tool uses this; the WPF shell provides its own WPF-MessageBox
/// adapter.
/// </summary>
public sealed class ConsoleUserNotifier : IUserNotifier
{
    public void Info(string message, string title)
    {
        Console.WriteLine($"[INFO]  {title}: {message}");
    }

    public void Warn(string message, string title)
    {
        Console.Error.WriteLine($"[WARN]  {title}: {message}");
    }

    public void ShowError(string message, string title)
    {
        Console.Error.WriteLine($"[ERROR] {title}: {message}");
    }

    public ConfirmResult Confirm(string message, string title, bool allowCancel = false)
    {
        var prompt = allowCancel ? "[y/N/c]" : "[y/N]";
        Console.Write($"{title}: {message} {prompt} ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant() ?? string.Empty;
        return response switch
        {
            "y" or "yes" or "s" or "sim" => ConfirmResult.Yes,
            "c" or "cancel" when allowCancel => ConfirmResult.Cancel,
            _ => ConfirmResult.No,
        };
    }
}
