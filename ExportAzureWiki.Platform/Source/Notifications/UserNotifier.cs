using ExportAzureWiki.Core.Notifications;

namespace ExportAzureWiki.Platform.Notifications;

/// <summary>
/// Process-wide ambient notifier. The host (WPF or CLI) calls
/// <see cref="Configure"/> at startup; everything in Platform that needs
/// to surface a message reads <see cref="Active"/>.
///
/// Defaults to <see cref="NullUserNotifier"/> so Platform stays usable in
/// tests and headless scenarios without a configured adapter.
/// </summary>
public static class UserNotifier
{
    private static IUserNotifier _active = NullUserNotifier.Instance;

    public static IUserNotifier Active => _active;

    public static void Configure(IUserNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _active = notifier;
    }

    /// <summary>Reset to <see cref="NullUserNotifier"/>. Test seam.</summary>
    internal static void Reset() => _active = NullUserNotifier.Instance;
}
