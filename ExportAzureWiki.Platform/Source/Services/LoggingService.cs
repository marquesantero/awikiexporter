using System.Text;

namespace ExportAzureWiki.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public static class LoggingService
{
    private static readonly string LogDirectory;
    private static readonly string LogFilePath;
    private static readonly object LogLock = new();

    static LoggingService()
    {
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExportAzureWiki",
            "Logs"
        );
        Directory.CreateDirectory(LogDirectory);
        
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        LogFilePath = Path.Combine(LogDirectory, $"app_{today}.log");
        
        CleanupOldLogs();
    }

    public static void LogDebug(string message, Exception? exception = null)
    {
        WriteLog(LogLevel.Debug, message, exception);
    }

    public static void LogInfo(string message, Exception? exception = null)
    {
        WriteLog(LogLevel.Info, message, exception);
    }

    public static void LogWarning(string message, Exception? exception = null)
    {
        WriteLog(LogLevel.Warning, message, exception);
    }

    public static void LogError(string message, Exception? exception = null)
    {
        WriteLog(LogLevel.Error, message, exception);
    }

    public static void LogCritical(string message, Exception? exception = null)
    {
        WriteLog(LogLevel.Critical, message, exception);
    }

    public static void LogExport(string operation, string details)
    {
        LogInfo($"EXPORT_{operation}: {details}");
    }

    public static void LogMemoryUsage(string operation)
    {
        var memoryBefore = GC.GetTotalMemory(false);
        GC.Collect();
        var memoryAfter = GC.GetTotalMemory(true);
        
        LogInfo($"MEMORY_{operation}: Before={FormatBytes(memoryBefore)}, After={FormatBytes(memoryAfter)}");
    }

    private static void WriteLog(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var threadId = Environment.CurrentManagedThreadId;
            
            var logEntry = new StringBuilder();
            logEntry.AppendLine($"[{timestamp}] [{level}] [Thread:{threadId}] {message}");
            
            if (exception != null)
            {
                logEntry.AppendLine($"Exception: {exception.GetType().Name}: {exception.Message}");
                logEntry.AppendLine($"StackTrace: {exception.StackTrace}");
                
                if (exception.InnerException != null)
                {
                    logEntry.AppendLine($"InnerException: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
                }
            }

            lock (LogLock)
            {
                File.AppendAllText(LogFilePath, logEntry.ToString());
            }

            // Also output to console for debugging
            if (level >= LogLevel.Warning)
            {
                Console.WriteLine($"[{level}] {message}");
                if (exception != null)
                {
                    Console.WriteLine($"Exception: {exception.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // Fallback to console if file logging fails
            Console.WriteLine($"Logging failed: {ex.Message}");
            Console.WriteLine($"Original message: [{level}] {message}");
        }
    }

    private static void CleanupOldLogs()
    {
        try
        {
            var cutoffDate = DateTime.Now.AddDays(-7);
            var logFiles = Directory.GetFiles(LogDirectory, "app_*.log");
            
            foreach (var logFile in logFiles)
            {
                var fileInfo = new FileInfo(logFile);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    File.Delete(logFile);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to cleanup old logs: {ex.Message}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double number = bytes;
        int suffixIndex = 0;

        while (number >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            number /= 1024;
            suffixIndex++;
        }

        return $"{number:F2} {suffixes[suffixIndex]}";
    }

    public static string GetLogDirectory() => LogDirectory;
    
    public static string GetTodayLogFile() => LogFilePath;
}