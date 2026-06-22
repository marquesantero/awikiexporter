using System.Windows;
using ExportAzureWiki.Platform;
using ExportAzureWiki.Platform.Notifications;
using ExportAzureWiki.Data;
using ExportAzureWiki.Data.Schema;
using ExportAzureWiki.Services;
using ExportAzureWiki.Platform.Setup;
using ExportAzureWiki.Wpf.Notifications;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Wpf.Views.Setup;
using ExportAzureWiki.Models;
using System.IO;

namespace ExportAzureWiki.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Last-resort handlers: log every unhandled exception (UI dispatcher,
        // background tasks, app domain) and keep the app alive where the UI
        // thread can recover, instead of crashing silently.
        HookGlobalExceptionHandlers();

        // The setup wizard is shown modally before any main window exists. With
        // the default OnLastWindowClose, closing the wizard (the only window)
        // triggers application shutdown even though we open the main window right
        // after -- so the app would close after "Start Application" and only work
        // on a second launch. Keep the process alive explicitly across the
        // wizard and restore OnLastWindowClose once the main window is up.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Wire localization (AppText -> LocalizationManager) BEFORE anything UI
        // shows. Without this the setup wizard renders with the raw resource
        // keys / English fallbacks because AppText is still unconfigured (it was
        // previously only initialized inside PlatformHost, which runs after the
        // wizard). Initialize() is idempotent.
        ExportAzureWiki.Platform.Bootstrap.StartupInitializer.Initialize();

        // Pin every WebView2 instance to a user-data folder under the app's
        // managed cache root (instead of the default location next to the exe),
        // so its on-disk cache of rendered content lives where the app can wipe
        // it and never leaks to a shared/OS-managed location. Must be set before
        // the first WebView2 is created.
        try
        {
            Directory.CreateDirectory(WikiCachePaths.WebView2);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", WikiCachePaths.WebView2);
        }
        catch
        {
            // Non-fatal: fall back to the default WebView2 user-data folder.
        }

        // Preview and Word/PDF export rely on the WebView2 Evergreen runtime.
        // Warn early (and clearly) on machines where it is missing instead of
        // failing obscurely later; non-fatal so the rest of the app still runs.
        WarnIfWebView2RuntimeMissing();

        // Install the WPF MessageBox adapter before Platform boots so the
        // setup wizard and the connection-restore path can surface errors
        // through the same surface the rest of the app uses.
        UserNotifier.Configure(new WpfUserNotifier());

        TryRestoreConnectionFromSavedToken();

        var setupService = BuildSetupService();
        if (RequiresInitialSetup(setupService))
        {
            var wizard = new SetupWizardWindow(setupService);
            var result = wizard.ShowDialog();
            if (result != true)
            {
                Shutdown();
                return;
            }
        }

        var services = PlatformHost.CreateServices();
        var mainViewModel = new MainViewModel(services);
        var window = new MainWindow(mainViewModel);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        window.Show();
    }

    private void HookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LoggingService.LogError("Unhandled UI exception.", args.Exception);
            ShowUnhandledExceptionDialog(args.Exception);
            // The UI thread can keep running; prevent a hard crash.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LoggingService.LogError("Unhandled non-UI exception.", ex);
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LoggingService.LogError("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    private static void ShowUnhandledExceptionDialog(Exception ex)
    {
        try
        {
            MessageBox.Show(
                string.Format(
                    ExportAzureWiki.Core.Localization.AppText.S(
                        "wpf.error.unhandled.message",
                        "An unexpected error occurred:\n\n{0}\n\nThe error was logged. You can keep working or restart the app."),
                    ex.Message),
                ExportAzureWiki.Core.Localization.AppText.S("wpf.error.unhandled.title", "Unexpected error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Never let the error dialog itself take the process down.
        }
    }

    private static void WarnIfWebView2RuntimeMissing()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return;
            }
        }
        catch
        {
            // GetAvailableBrowserVersionString throws when no runtime is found.
        }

        MessageBox.Show(
            ExportAzureWiki.Core.Localization.AppText.S(
                "wpf.webview2.missing.message",
                "The Microsoft Edge WebView2 Runtime was not found. Page preview and Word/PDF export need it. Please install the Evergreen WebView2 Runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and restart the app."),
            ExportAzureWiki.Core.Localization.AppText.S("wpf.webview2.missing.title", "WebView2 Runtime missing"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static ISetupService BuildSetupService()
    {
        var connectionFactory = new DbConnectionFactory();
        var schemaManager = new SchemaManager(connectionFactory);
        var hashing = new PasswordHashingService();
        return new SetupService(connectionFactory, schemaManager, hashing);
    }

    private static bool RequiresInitialSetup(ISetupService setupService)
    {
        try
        {
            var adminExists = setupService.AdminUserExistsAsync().GetAwaiter().GetResult();
            if (adminExists)
            {
                if (SetupChecker.IsFirstRun())
                {
                    SetupChecker.MarkSetupComplete();
                }

                return false;
            }

            if (!SetupChecker.IsFirstRun() && SetupChecker.IsDatabaseConfigured())
            {
                return false;
            }

            return true;
        }
        catch
        {
            return SetupChecker.IsFirstRun();
        }
    }

    private static void TryRestoreConnectionFromSavedToken()
    {
        try
        {
            var tokenStore = new ConnectionBootstrapTokenStore();
            if (!tokenStore.TryLoad(out var token))
            {
                return;
            }

            var tokenService = new ConnectionBootstrapTokenService();
            if (!tokenService.TryReadToken(token, out var config, out _) || config == null)
            {
                return;
            }

            var factory = new DbConnectionFactory();
            factory.SetConnectionFromConfig(config);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"WPF startup token restore failed: {ex.Message}");
        }
    }

}



