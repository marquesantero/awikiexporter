using System.Security.Cryptography;
using System.Text;
using ExportAzureWiki;

namespace ExportAzureWiki.Tests.Security;

[Collection(TempKeyCollection.Name)]
public sealed class EncryptionHelperTests
{
    private readonly TempKeyFixture _fixture;

    public EncryptionHelperTests(TempKeyFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrips_The_Plaintext()
    {
        const string plaintext = "azure-devops-PAT-with-symbols !@#$%^&*()_+ áéíóú";

        var cipher = EncryptionHelper.Encrypt(plaintext);
        var decoded = EncryptionHelper.Decrypt(cipher);

        decoded.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_Empty_Returns_Empty_Without_Touching_The_Key()
    {
        EncryptionHelper.Encrypt(string.Empty).Should().BeEmpty();
        EncryptionHelper.Decrypt(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_Produces_New_Format_With_Gcm_Marker()
    {
        var cipher = EncryptionHelper.Encrypt("anything");
        var blob = Convert.FromBase64String(cipher);

        blob[0].Should().Be(0x02, "the new GCM payload is tagged with 0x02");
        // [marker(1) + nonce(12) + ciphertext(>=1) + tag(16)] for non-empty input.
        blob.Length.Should().BeGreaterThan(1 + 12 + 16);
    }

    [Fact]
    public void Encrypt_Of_Same_Plaintext_Produces_Different_Ciphertext()
    {
        // AES-GCM uses a random nonce per call, so identical plaintexts
        // must never produce identical ciphertext (would leak repeats).
        var first = EncryptionHelper.Encrypt("repeat");
        var second = EncryptionHelper.Encrypt("repeat");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Tampered_Ciphertext_Fails_Decrypt()
    {
        var cipher = EncryptionHelper.Encrypt("sensitive");
        var blob = Convert.FromBase64String(cipher);

        // Flip one bit inside the ciphertext region (skip the format marker
        // and the nonce). GCM must reject this.
        blob[1 + 12 + 2] ^= 0x01;
        var tampered = Convert.ToBase64String(blob);

        var act = () => EncryptionHelper.Decrypt(tampered);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Tampered_Tag_Fails_Decrypt()
    {
        var cipher = EncryptionHelper.Encrypt("sensitive");
        var blob = Convert.FromBase64String(cipher);

        // Flip the last byte (inside the auth tag).
        blob[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(blob);

        var act = () => EncryptionHelper.Decrypt(tampered);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Invalid_Base64_Throws_Invalid_Operation()
    {
        var act = () => EncryptionHelper.Decrypt("not base 64 !!!");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EncryptBytes_Then_DecryptBytes_RoundTrips_Binary()
    {
        // Bytes that are not valid UTF-8 and include a 0x00 and the 'e' (0x65)
        // byte the cache prefix starts with -- proves binary fidelity.
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x65, 0x6E, 0x63, 0xFF, 0xD8, 0x01 };

        var blob = EncryptionHelper.EncryptBytes(data);
        EncryptionHelper.DecryptBytes(blob).Should().Equal(data);
    }

    [Fact]
    public void EncryptBytes_Tags_Gcm_And_Hides_The_Plaintext()
    {
        var data = Encoding.ASCII.GetBytes("CONFIDENTIAL-IMAGE-BYTES");

        var blob = EncryptionHelper.EncryptBytes(data);

        blob[0].Should().Be(0x02, "the GCM payload is tagged with 0x02");
        blob.Length.Should().BeGreaterThan(1 + 12 + 16);
        // The plaintext must not survive anywhere inside the ciphertext blob.
        Convert.ToBase64String(blob).Should().NotContain(Convert.ToBase64String(data));
    }

    [Fact]
    public void EncryptBytes_Empty_Returns_Empty_Without_Touching_The_Key()
    {
        EncryptionHelper.EncryptBytes([]).Should().BeEmpty();
        EncryptionHelper.DecryptBytes([]).Should().BeEmpty();
    }

    [Fact]
    public void DecryptBytes_Tampered_Blob_Throws()
    {
        var blob = EncryptionHelper.EncryptBytes(Encoding.ASCII.GetBytes("sensitive image"));

        // Flip a bit inside the ciphertext region (past marker + nonce).
        blob[1 + 12 + 1] ^= 0x01;

        var act = () => EncryptionHelper.DecryptBytes(blob);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Legacy_Cbc_Blob_Is_Decrypted_For_Migration()
    {
        // Reproduce a legacy AES-CBC blob using the exact same master key
        // EncryptionHelper would resolve from the fixture's key directory.
        var key = ReadProtectedMasterKey(_fixture.KeyDirectory);
        var legacyBlob = ProduceLegacyCbcBlob(key, "legacy plaintext");

        var decoded = EncryptionHelper.Decrypt(legacyBlob);

        decoded.Should().Be("legacy plaintext");
    }

    private static byte[] ReadProtectedMasterKey(string keyDirectory)
    {
        // Force the helper to materialize a key, then read it back.
        _ = EncryptionHelper.Encrypt("warmup");
        var path = Path.Combine(keyDirectory, "key.dat");
        var protectedKey = File.ReadAllBytes(path);
        return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
    }

    private static string ProduceLegacyCbcBlob(byte[] key, string plaintext)
    {
        var iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        ms.Write(iv, 0, iv.Length);
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plaintext);
        }
        return Convert.ToBase64String(ms.ToArray());
    }
}
