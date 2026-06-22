using System.Security.Cryptography;
using System.Text;

namespace ExportAzureWiki;

/// <summary>
/// Encrypts and decrypts secrets at rest using AES-GCM authenticated encryption.
/// The master key is generated per Windows user, stored under
/// %LocalAppData%\ExportAzureWiki\key.dat, and protected with DPAPI
/// (CurrentUser scope) so it can only be read back by the same user on the
/// same machine.
///
/// Wire format for new ciphertexts:
///   [ formatByte(1) | nonce(12) | ciphertext(N) | tag(16) ]
/// Base64-encoded for storage.
///
/// Legacy AES-CBC blobs written by previous releases are still readable so
/// existing sessions and stored secrets keep working; once a value passes
/// through Decrypt+Encrypt again it is rewritten in the new GCM format.
/// </summary>
public static class EncryptionHelper
{
    private const int KeySize = 32;        // 256-bit key
    private const int NonceSize = 12;      // AES-GCM standard nonce
    private const int TagSize = 16;        // AES-GCM standard tag
    private const byte FormatGcm = 0x02;   // marks a new GCM payload

    private const int LegacyCbcIvSize = 16; // AES-CBC IV used by old releases
    private const int CbcBlockSize = 16;

    /// <summary>
    /// Test seam: when set, the master key is read from / written to this
    /// directory instead of %LocalAppData%\ExportAzureWiki. Production code
    /// must never touch this; only the test project (InternalsVisibleTo)
    /// uses it to isolate runs in a temp directory.
    /// </summary>
    internal static string? KeyDirectoryOverride { get; set; }

    private static byte[] GetOrCreateKey()
    {
        var keyDir = KeyDirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExportAzureWiki");
        var keyPath = Path.Combine(keyDir, "key.dat");

        if (!Directory.Exists(keyDir))
        {
            Directory.CreateDirectory(keyDir);
        }

        if (File.Exists(keyPath))
        {
            var protectedKey = File.ReadAllBytes(keyPath);
            return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
        }

        var newKey = RandomNumberGenerator.GetBytes(KeySize);
        var protectedNewKey = ProtectedData.Protect(newKey, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(keyPath, protectedNewKey);
        return newKey;
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
        var ciphertextBytes = new byte[plaintextBytes.Length];
        var tagBytes = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertextBytes, tagBytes);
        }

        var output = new byte[1 + NonceSize + ciphertextBytes.Length + TagSize];
        output[0] = FormatGcm;
        Buffer.BlockCopy(nonce, 0, output, 1, NonceSize);
        Buffer.BlockCopy(ciphertextBytes, 0, output, 1 + NonceSize, ciphertextBytes.Length);
        Buffer.BlockCopy(tagBytes, 0, output, 1 + NonceSize + ciphertextBytes.Length, TagSize);

        return Convert.ToBase64String(output);
    }

    /// <summary>
    /// Encrypts raw bytes (e.g. a cached image) into a self-contained GCM blob
    /// <c>[ formatByte(1) | nonce(12) | ciphertext(N) | tag(16) ]</c>. Unlike
    /// <see cref="Encrypt(string)"/> the result is left as raw bytes (no Base64),
    /// which keeps binary cache files compact.
    /// </summary>
    public static byte[] EncryptBytes(byte[] plaintext)
    {
        if (plaintext == null || plaintext.Length == 0)
        {
            return [];
        }

        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var output = new byte[1 + NonceSize + ciphertext.Length + TagSize];
        output[0] = FormatGcm;
        Buffer.BlockCopy(nonce, 0, output, 1, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, output, 1 + NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, output, 1 + NonceSize + ciphertext.Length, TagSize);
        return output;
    }

    /// <summary>
    /// Reverses <see cref="EncryptBytes"/>. These binary blobs are always GCM
    /// (the format post-dates the legacy CBC era), so there is no CBC fallback;
    /// a tag mismatch or truncated blob throws.
    /// </summary>
    public static byte[] DecryptBytes(byte[] blob)
    {
        if (blob == null || blob.Length == 0)
        {
            return [];
        }

        if (blob[0] != FormatGcm || blob.Length < 1 + NonceSize + TagSize)
        {
            throw new InvalidOperationException("Cipher data is not a valid GCM payload.");
        }

        var key = GetOrCreateKey();
        var ciphertextLength = blob.Length - 1 - NonceSize - TagSize;
        var nonce = new byte[NonceSize];
        var ciphertext = new byte[ciphertextLength];
        var tag = new byte[TagSize];
        var plaintext = new byte[ciphertextLength];

        Buffer.BlockCopy(blob, 1, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, 1 + NonceSize, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(blob, 1 + NonceSize + ciphertextLength, tag, 0, TagSize);

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Failed to decrypt data. The authentication tag did not match; the data may have been tampered with.", ex);
        }

        return plaintext;
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(cipherText);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Cipher text is not valid base64.", ex);
        }

        if (blob.Length == 0)
        {
            throw new InvalidOperationException("Cipher text is empty.");
        }

        var key = GetOrCreateKey();

        // New GCM blobs always start with FormatGcm. Legacy CBC blobs begin
        // with a 16-byte IV whose first byte is random, so 1/256 of them will
        // collide on the format byte. If GCM authentication fails on such a
        // collision, fall back to CBC when the length still fits the CBC
        // layout. Real tampering on a real GCM payload still surfaces as
        // CryptographicException through the legacy branch.
        if (blob[0] == FormatGcm && blob.Length >= 1 + NonceSize + TagSize)
        {
            try
            {
                return DecryptGcm(blob, key);
            }
            catch (CryptographicException)
            {
                if (LooksLikeLegacyCbc(blob))
                {
                    return DecryptLegacyCbc(blob, key);
                }
                throw new InvalidOperationException(
                    "Failed to decrypt data. The authentication tag did not match; the data may have been tampered with.");
            }
        }

        return DecryptLegacyCbc(blob, key);
    }

    private static string DecryptGcm(byte[] blob, byte[] key)
    {
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = blob.Length - 1 - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];
        var plaintext = new byte[ciphertextLength];

        Buffer.BlockCopy(blob, 1, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, 1 + NonceSize, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(blob, 1 + NonceSize + ciphertextLength, tag, 0, TagSize);

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static bool LooksLikeLegacyCbc(byte[] blob)
    {
        if (blob.Length <= LegacyCbcIvSize)
        {
            return false;
        }
        return (blob.Length - LegacyCbcIvSize) % CbcBlockSize == 0;
    }

    private static string DecryptLegacyCbc(byte[] blob, byte[] key)
    {
        if (blob.Length < LegacyCbcIvSize + CbcBlockSize)
        {
            throw new InvalidOperationException("Cipher text is too short to be a valid legacy CBC payload.");
        }

        try
        {
            var iv = new byte[LegacyCbcIvSize];
            var cipher = new byte[blob.Length - LegacyCbcIvSize];

            Buffer.BlockCopy(blob, 0, iv, 0, LegacyCbcIvSize);
            Buffer.BlockCopy(blob, LegacyCbcIvSize, cipher, 0, cipher.Length);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var memoryStream = new MemoryStream(cipher);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cryptoStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to decrypt data. The data may be corrupted or encrypted with a different key.", ex);
        }
    }
}
