using ExportAzureWiki.Services;

namespace ExportAzureWiki.Tests.Security;

[Collection(TempKeyCollection.Name)]
public sealed class StoredSecretTests
{
    [Fact]
    public void Protect_Then_Reveal_Round_Trips_The_Plaintext()
    {
        var protectedValue = StoredSecret.Protect("super-secret-PAT");
        StoredSecret.Reveal(protectedValue).Should().Be("super-secret-PAT");
    }

    [Fact]
    public void Protect_Tags_The_Output_With_The_Enc_Prefix()
    {
        var protectedValue = StoredSecret.Protect("any value");
        protectedValue.Should().StartWith("enc:");
    }

    [Fact]
    public void Protect_Of_Empty_Returns_Empty()
    {
        StoredSecret.Protect(string.Empty).Should().BeEmpty();
        StoredSecret.Protect(null).Should().BeEmpty();
    }

    [Fact]
    public void Protect_Idempotent_For_Already_Protected_Value()
    {
        var once = StoredSecret.Protect("secret");
        var twice = StoredSecret.Protect(once);

        twice.Should().Be(once, "double-protecting must not bury the value under two layers of encryption");
    }

    [Fact]
    public void Reveal_Of_Legacy_Plaintext_Returns_As_Is()
    {
        // Rows written before Fase 1.6 do not have the "enc:" prefix.
        StoredSecret.Reveal("legacy-pat-with-no-prefix").Should().Be("legacy-pat-with-no-prefix");
    }

    [Fact]
    public void Reveal_Of_Empty_Returns_Empty()
    {
        StoredSecret.Reveal(string.Empty).Should().BeEmpty();
        StoredSecret.Reveal(null).Should().BeEmpty();
    }

    [Fact]
    public void Reveal_Of_Corrupt_Encrypted_Blob_Returns_Empty()
    {
        // Caller must surface a clean "reauthenticate" error and never
        // forward a broken token; Reveal returns empty instead of throwing.
        StoredSecret.Reveal("enc:not-a-valid-blob").Should().BeEmpty();
    }
}
