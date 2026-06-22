using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Services.Authentication;
using Serilog;

namespace ExportAzureWiki.Services;

/// <summary>
/// Builds a self-contained ZIP an operator can ship back to the
/// maintainers to investigate a problem without exposing secrets.
///
/// The bundle contains:
///   manifest.json       app version, runtime, OS, culture
///   system.json         configuration paths that exist (no secrets)
///   logs/*.log          last 14 daily Serilog files (already masked
///                       by the SensitivePropertyEnricher at write time)
///   audit-summary.txt   counts of recent security audit events plus a
///                       sample of the latest entries (no full detail)
///
/// The bundle explicitly EXCLUDES:
///   - The DPAPI-protected AES key file (key.dat)
///   - The MSAL token cache
///   - The wiki content cache (pages, images)
///   - Any DB connection string
///
/// See <c>docs/operations/RUNBOOK.md</c> for the operator-facing usage.
/// </summary>
public sealed class DiagnosticBundleService
{
    private const int MaxLogFiles = 14;
    private const int MaxAuditSampleSize = 100;

    private readonly string _logsDirectory;
    private readonly SecurityAuditService? _audit;

    public DiagnosticBundleService(SecurityAuditService? audit = null, string? logsDirectory = null)
    {
        _audit = audit;
        _logsDirectory = logsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExportAzureWiki",
            "Logs");
    }

    /// <summary>
    /// Writes the bundle ZIP to <paramref name="outputPath"/>. Creates the
    /// containing directory if it does not exist. Overwrites the file.
    /// </summary>
    public async Task CreateBundleAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullPath = Path.GetFullPath(outputPath);
        var outputDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Closing the FileStream too early during exceptional flow would
        // leave a 0-byte file behind; the using ordering here is the
        // standard ZipArchive cookbook pattern.
        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteManifestAsync(archive, cancellationToken);
            await WriteSystemDescriptorAsync(archive, cancellationToken);
            CopyLogFiles(archive);
            await WriteAuditSummaryAsync(archive, cancellationToken);
        }

        Log.Information("Diagnostic bundle written to {OutputPath}", fullPath);
    }

    private static async Task WriteManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var assembly = typeof(DiagnosticBundleService).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        var manifest = new
        {
            generatedAtUtc = DateTime.UtcNow,
            application = new
            {
                name = "ExportAzureWiki",
                assemblyVersion = assembly.GetName().Version?.ToString(),
                fileVersion,
                informationalVersion = informational,
            },
            runtime = new
            {
                framework = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            },
            culture = new
            {
                current = CultureInfo.CurrentCulture.Name,
                ui = CultureInfo.CurrentUICulture.Name,
            }
        };

        await WriteJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken);
    }

    private async Task WriteSystemDescriptorAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "ExportAzureWiki");

        // The descriptor reports the *existence* of each path, never its
        // contents. An operator (or maintainer reading the bundle) can
        // tell whether the master key is provisioned without ever seeing
        // the key itself.
        var descriptor = new
        {
            paths = new
            {
                appFolderExists = Directory.Exists(appFolder),
                masterKeyFileExists = File.Exists(Path.Combine(appFolder, "key.dat")),
                msalCacheFileExists = File.Exists(Path.Combine(appFolder, "MsalCache", "ExportAzureWiki.msal.cache")),
                logsFolderExists = Directory.Exists(_logsDirectory),
                logsFolder = _logsDirectory,
            },
        };

        await WriteJsonEntryAsync(archive, "system.json", descriptor, cancellationToken);
    }

    private void CopyLogFiles(ZipArchive archive)
    {
        if (!Directory.Exists(_logsDirectory))
        {
            return;
        }

        var files = Directory
            .GetFiles(_logsDirectory, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(MaxLogFiles)
            .ToList();

        foreach (var file in files)
        {
            // Reading via a shared stream avoids the lock the daily sink
            // holds while the app is still writing the file.
            using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var entry = archive.CreateEntry($"logs/{file.Name}", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            source.CopyTo(entryStream);
        }
    }

    private async Task WriteAuditSummaryAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return;
        }

        IReadOnlyList<SecurityAuditEntry> entries;
        try
        {
            entries = await _audit.ListRecentAsync(MaxAuditSampleSize);
        }
        catch (Exception ex)
        {
            // The bundle should still ship even if the audit table is
            // unreachable; log and write a placeholder so the absence is
            // explicit.
            Log.Warning(ex, "Diagnostic bundle: audit summary skipped (audit lookup failed)");
            var entry = archive.CreateEntry("audit-summary.txt");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            await writer.WriteLineAsync("Audit lookup failed; see ops log for details.");
            return;
        }

        var counts = entries
            .GroupBy(e => e.EventType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"sample size: {entries.Count}");
        sb.AppendLine();
        sb.AppendLine("counts by event type:");
        foreach (var kvp in counts.OrderByDescending(k => k.Value))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {kvp.Value,6}  {kvp.Key}");
        }

        sb.AppendLine();
        sb.AppendLine("latest entries (newest first):");
        foreach (var e in entries.Take(20))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  {e.OccurredAt:o}  {e.EventType,-24}  user={e.Username ?? "-"}");
        }

        var summaryEntry = archive.CreateEntry("audit-summary.txt");
        using var summaryWriter = new StreamWriter(summaryEntry.Open(), new UTF8Encoding(false));
        await summaryWriter.WriteAsync(sb.ToString());
    }

    private static async Task WriteJsonEntryAsync(ZipArchive archive, string entryName, object payload, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        }, cancellationToken);
    }
}
