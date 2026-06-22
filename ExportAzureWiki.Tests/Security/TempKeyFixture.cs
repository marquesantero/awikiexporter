using ExportAzureWiki;

namespace ExportAzureWiki.Tests.Security;

/// <summary>
/// Routes EncryptionHelper's master key into a per-fixture temp directory
/// so encryption tests do not touch the developer's real LocalApplicationData
/// and remain isolated when xunit parallelizes across collections.
/// </summary>
public sealed class TempKeyFixture : IDisposable
{
    public string KeyDirectory { get; }

    public TempKeyFixture()
    {
        KeyDirectory = Path.Combine(Path.GetTempPath(), "ExportAzureWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(KeyDirectory);
        EncryptionHelper.KeyDirectoryOverride = KeyDirectory;
    }

    public void Dispose()
    {
        EncryptionHelper.KeyDirectoryOverride = null;
        try
        {
            if (Directory.Exists(KeyDirectory))
            {
                Directory.Delete(KeyDirectory, recursive: true);
            }
        }
        catch
        {
            // Cleanup is best-effort; the OS will eventually GC temp.
        }
    }
}

/// <summary>
/// All tests that exercise EncryptionHelper must share this collection so
/// the KeyDirectoryOverride is set, the master key file is reused across
/// roundtrips within a test class, and tests do not race over the override
/// global.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TempKeyCollection : ICollectionFixture<TempKeyFixture>
{
    public const string Name = "TempKey";
}
