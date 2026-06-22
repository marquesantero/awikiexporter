using Serilog;
using Serilog.Events;

namespace ExportAzureWiki.Platform.Logging;

/// <summary>
/// Owns the lifecycle of the global Serilog logger used by the Platform.
/// Initialize() is idempotent: it picks up the default configuration once
/// and stays out of the way for subsequent calls (test code can reinit by
/// calling Reset()).
/// </summary>
public static class PlatformLogging
{
    private static readonly object _gate = new();
    private static bool _initialized;

    /// <summary>
    /// Configures Log.Logger with a rolling file sink under
    /// %LocalAppData%\ExportAzureWiki\Logs and a Trace sink so existing
    /// listeners (Visual Studio, DebugView, CI test logs) keep working.
    /// All events pass through SensitivePropertyEnricher so secrets never
    /// reach a sink.
    /// </summary>
    public static void Initialize()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ExportAzureWiki",
                "Logs");
            Directory.CreateDirectory(logsDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.With(new SensitivePropertyEnricher())
                .Enrich.WithProperty("Application", "ExportAzureWiki")
                .WriteTo.File(
                    path: Path.Combine(logsDir, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 25 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffzzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.Trace(restrictedToMinimumLevel: LogEventLevel.Warning)
                .CreateLogger();

            _initialized = true;
        }
    }

    /// <summary>
    /// Test seam: reset the configured logger so a subsequent Initialize()
    /// rebuilds it. Production code must not call this.
    /// </summary>
    internal static void Reset()
    {
        lock (_gate)
        {
            Log.CloseAndFlush();
            _initialized = false;
        }
    }
}
