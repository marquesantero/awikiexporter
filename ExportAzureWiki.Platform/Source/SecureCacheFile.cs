using System.IO;
using System.Text;

namespace ExportAzureWiki;

/// <summary>
/// Reads/writes app cache files with transparent at-rest encryption. Content is
/// protected with <see cref="EncryptionHelper"/> (AES-GCM under a DPAPI-sealed,
/// per-user key), so a cached file copied off the machine -- or read by another
/// Windows user -- is useless. The on-disk form is tagged with
/// <see cref="EncryptedPrefix"/>; a file without the tag is treated as legacy
/// plaintext (still readable) and is re-encrypted on the next write.
/// </summary>
public static class SecureCacheFile
{
    private const string EncryptedPrefix = "enc:";

    // Byte-level tag for encrypted binary cache files (images). Real image
    // formats never begin with these four ASCII bytes (PNG starts 0x89 'P',
    // JPEG 0xFF 0xD8, GIF "GIF8", WebP "RIFF"), so an untagged file is safely
    // treated as legacy plaintext.
    private static readonly byte[] EncryptedBinaryPrefix = Encoding.ASCII.GetBytes("enc:");

    public static async Task WriteTextAsync(string path, string content)
    {
        var payload = EncryptedPrefix + EncryptionHelper.Encrypt(content ?? string.Empty);
        await File.WriteAllTextAsync(path, payload).ConfigureAwait(false);
    }

    public static async Task<string> ReadTextAsync(string path)
    {
        var raw = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        return Decode(raw);
    }

    /// <summary>
    /// Writes binary cache content (e.g. a downloaded image) encrypted at rest.
    /// The on-disk form is <see cref="EncryptedBinaryPrefix"/> followed by the
    /// raw GCM blob from <see cref="EncryptionHelper.EncryptBytes"/>.
    /// </summary>
    public static async Task WriteBytesAsync(string path, byte[] data)
    {
        var cipher = EncryptionHelper.EncryptBytes(data ?? []);
        var payload = new byte[EncryptedBinaryPrefix.Length + cipher.Length];
        Buffer.BlockCopy(EncryptedBinaryPrefix, 0, payload, 0, EncryptedBinaryPrefix.Length);
        Buffer.BlockCopy(cipher, 0, payload, EncryptedBinaryPrefix.Length, cipher.Length);
        await File.WriteAllBytesAsync(path, payload).ConfigureAwait(false);
    }

    /// <summary>Reads and decrypts a binary cache file (encrypted or legacy plaintext).</summary>
    public static byte[] ReadBytes(string path) => DecodeBytes(File.ReadAllBytes(path));

    /// <summary>Async counterpart to <see cref="ReadBytes"/>.</summary>
    public static async Task<byte[]> ReadBytesAsync(string path) =>
        DecodeBytes(await File.ReadAllBytesAsync(path).ConfigureAwait(false));

    /// <summary>Decodes a stored binary cache payload (encrypted or legacy plaintext).</summary>
    internal static byte[] DecodeBytes(byte[]? raw)
    {
        if (raw == null || raw.Length == 0)
        {
            return [];
        }

        if (!HasBinaryPrefix(raw))
        {
            // Legacy plaintext image written before at-rest encryption: return
            // as-is; the next write re-encrypts it.
            return raw;
        }

        try
        {
            var blob = new byte[raw.Length - EncryptedBinaryPrefix.Length];
            Buffer.BlockCopy(raw, EncryptedBinaryPrefix.Length, blob, 0, blob.Length);
            return EncryptionHelper.DecryptBytes(blob);
        }
        catch
        {
            // Corrupted blob, or a key from another user/machine: treat as a
            // cache miss rather than surfacing a hard failure.
            return [];
        }
    }

    private static bool HasBinaryPrefix(byte[] raw)
    {
        if (raw.Length < EncryptedBinaryPrefix.Length)
        {
            return false;
        }

        for (var i = 0; i < EncryptedBinaryPrefix.Length; i++)
        {
            if (raw[i] != EncryptedBinaryPrefix[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Decodes a stored cache payload (encrypted or legacy plaintext).</summary>
    internal static string Decode(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        if (!raw.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            // Legacy plaintext written before at-rest encryption: return as-is;
            // the next write re-encrypts it.
            return raw;
        }

        try
        {
            return EncryptionHelper.Decrypt(raw[EncryptedPrefix.Length..]);
        }
        catch
        {
            // Corrupted blob, or a key from another user/machine: treat as a
            // cache miss rather than surfacing a hard failure.
            return string.Empty;
        }
    }
}
