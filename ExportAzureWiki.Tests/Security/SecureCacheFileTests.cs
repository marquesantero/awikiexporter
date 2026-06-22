using System.Text;
using ExportAzureWiki;

namespace ExportAzureWiki.Tests.Security;

/// <summary>
/// Verifies the cache files written to disk are encrypted at rest and still
/// round-trip, and that pre-encryption plaintext caches stay readable.
/// </summary>
[Collection(TempKeyCollection.Name)]
public sealed class SecureCacheFileTests
{
    [Fact]
    public async Task WriteThenRead_RoundTripsContent()
    {
        var path = TempFile();
        try
        {
            const string content = "# Secret wiki page\n\nConfidential corporate content.";
            await SecureCacheFile.WriteTextAsync(path, content);

            (await SecureCacheFile.ReadTextAsync(path)).Should().Be(content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OnDisk_IsEncrypted_NotPlaintext()
    {
        var path = TempFile();
        try
        {
            const string secret = "Confidential-marker-XYZ";
            await SecureCacheFile.WriteTextAsync(path, $"# Page\n{secret}\n");

            var onDisk = await File.ReadAllTextAsync(path);
            onDisk.Should().StartWith("enc:");
            onDisk.Should().NotContain(secret, "the cache content must not sit in plaintext on disk");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Decode_LegacyPlaintext_ReturnedAsIs()
    {
        // A cache file written before at-rest encryption (no "enc:" tag).
        SecureCacheFile.Decode("# Legacy page\nplain text").Should().Be("# Legacy page\nplain text");
    }

    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        SecureCacheFile.Decode(null).Should().BeEmpty();
        SecureCacheFile.Decode(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteBytes_ThenReadBytes_RoundTripsImage()
    {
        var path = TempFile(".png");
        try
        {
            var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF, 0x42 };
            await SecureCacheFile.WriteBytesAsync(path, image);

            SecureCacheFile.ReadBytes(path).Should().Equal(image);
            (await SecureCacheFile.ReadBytesAsync(path)).Should().Equal(image);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteBytes_OnDisk_IsEncrypted_NotPlaintext()
    {
        var path = TempFile(".png");
        try
        {
            var marker = Encoding.ASCII.GetBytes("SECRET-IMG-MARKER");
            await SecureCacheFile.WriteBytesAsync(path, marker);

            var onDisk = await File.ReadAllBytesAsync(path);
            Encoding.ASCII.GetString(onDisk, 0, 4).Should().Be("enc:");
            IndexOf(onDisk, marker).Should().Be(-1, "the image bytes must not sit in plaintext on disk");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecodeBytes_LegacyPlaintextImage_ReturnedAsIs()
    {
        // A PNG written before at-rest encryption (no "enc:" tag).
        var legacy = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        SecureCacheFile.DecodeBytes(legacy).Should().Equal(legacy);
    }

    [Fact]
    public void DecodeBytes_Empty_ReturnsEmpty()
    {
        SecureCacheFile.DecodeBytes(null).Should().BeEmpty();
        SecureCacheFile.DecodeBytes([]).Should().BeEmpty();
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { found = false; break; }
            }
            if (found) { return i; }
        }
        return -1;
    }

    private static string TempFile(string extension = ".md")
    {
        var dir = Path.Combine(Path.GetTempPath(), "ExportAzureWiki.Tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + extension);
    }
}
