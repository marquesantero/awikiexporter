using System.IO.Compression;
using System.Text.Json;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Verifies the bundle contains the entries the RUNBOOK promises and
/// excludes the items the SECURITY model promises (master key, MSAL
/// cache, wiki content cache).
/// </summary>
public sealed class DiagnosticBundleServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _logsDir;

    public DiagnosticBundleServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "ExportAzureWiki.Tests.diag-" + Guid.NewGuid().ToString("N"));
        _logsDir = Path.Combine(_workDir, "Logs");
        Directory.CreateDirectory(_logsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Creates_A_Valid_Zip_With_Required_Entries()
    {
        var bundle = Path.Combine(_workDir, "diag.zip");
        var service = new DiagnosticBundleService(audit: null, logsDirectory: _logsDir);

        await service.CreateBundleAsync(bundle);

        File.Exists(bundle).Should().BeTrue();
        using var zip = ZipFile.OpenRead(bundle);
        zip.Entries.Select(e => e.FullName).Should()
            .Contain("manifest.json")
            .And.Contain("system.json");
    }

    [Fact]
    public async Task Manifest_Contains_Application_And_Runtime_Sections()
    {
        var bundle = Path.Combine(_workDir, "diag.zip");
        var service = new DiagnosticBundleService(audit: null, logsDirectory: _logsDir);

        await service.CreateBundleAsync(bundle);

        using var zip = ZipFile.OpenRead(bundle);
        using var stream = zip.GetEntry("manifest.json")!.Open();
        var json = JsonDocument.Parse(stream);

        json.RootElement.TryGetProperty("application", out var app).Should().BeTrue();
        app.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("ExportAzureWiki");

        json.RootElement.TryGetProperty("runtime", out var rt).Should().BeTrue();
        rt.TryGetProperty("framework", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Includes_Recent_Log_Files()
    {
        File.WriteAllText(Path.Combine(_logsDir, "app-20260101.log"), "line 1\n");
        File.WriteAllText(Path.Combine(_logsDir, "app-20260102.log"), "line 2\n");

        var bundle = Path.Combine(_workDir, "diag.zip");
        var service = new DiagnosticBundleService(audit: null, logsDirectory: _logsDir);

        await service.CreateBundleAsync(bundle);

        using var zip = ZipFile.OpenRead(bundle);
        zip.Entries.Select(e => e.FullName).Should()
            .Contain("logs/app-20260101.log")
            .And.Contain("logs/app-20260102.log");
    }

    [Fact]
    public async Task Handles_Missing_Logs_Directory_Gracefully()
    {
        Directory.Delete(_logsDir, recursive: true);
        var bundle = Path.Combine(_workDir, "diag.zip");
        var service = new DiagnosticBundleService(audit: null, logsDirectory: _logsDir);

        await service.CreateBundleAsync(bundle);

        File.Exists(bundle).Should().BeTrue();
        using var zip = ZipFile.OpenRead(bundle);
        zip.Entries.Any(e => e.FullName.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse("no logs were present at bundle time");
    }

    [Fact]
    public async Task Does_Not_Embed_Master_Key_Or_Msal_Cache()
    {
        var bundle = Path.Combine(_workDir, "diag.zip");
        var service = new DiagnosticBundleService(audit: null, logsDirectory: _logsDir);

        await service.CreateBundleAsync(bundle);

        using var zip = ZipFile.OpenRead(bundle);
        var entryNames = zip.Entries.Select(e => e.FullName).ToList();

        entryNames.Should().NotContain(n => n.Contains("key.dat", StringComparison.OrdinalIgnoreCase));
        entryNames.Should().NotContain(n => n.Contains("msal.cache", StringComparison.OrdinalIgnoreCase));
        entryNames.Should().NotContain(n => n.Contains("WikiPages", StringComparison.OrdinalIgnoreCase));
        entryNames.Should().NotContain(n => n.Contains("WikiImages", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task System_Descriptor_Reports_Path_Existence_Without_Contents()
    {
        var bundle = Path.Combine(_workDir, "diag.zip");
        var service = new DiagnosticBundleService(audit: null, logsDirectory: _logsDir);

        await service.CreateBundleAsync(bundle);

        using var zip = ZipFile.OpenRead(bundle);
        using var stream = zip.GetEntry("system.json")!.Open();
        var json = JsonDocument.Parse(stream);

        json.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue();
        // Boolean flags only -- the report shows existence but never
        // contents. Whether the test runner happens to have a real
        // master key on disk is irrelevant; what matters is that the
        // descriptor surface is boolean.
        paths.TryGetProperty("masterKeyFileExists", out var key).Should().BeTrue();
        new[] { JsonValueKind.True, JsonValueKind.False }.Should().Contain(key.ValueKind);
        paths.TryGetProperty("msalCacheFileExists", out var msal).Should().BeTrue();
        new[] { JsonValueKind.True, JsonValueKind.False }.Should().Contain(msal.ValueKind);
    }
}
