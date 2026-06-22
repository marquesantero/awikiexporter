using ExportAzureWiki.Services;

namespace ExportAzureWiki.Tests.Security;

public sealed class PasswordHashingServiceTests
{
    private readonly PasswordHashingService _service = new();

    [Fact]
    public void HashPassword_Produces_Different_Hashes_For_Same_Password()
    {
        // Different salts per call => same plaintext must not collide.
        var first = _service.HashPassword("Sup3r$ecure!");
        var second = _service.HashPassword("Sup3r$ecure!");

        first.salt.Should().NotBe(second.salt);
        first.hash.Should().NotBe(second.hash);
    }

    [Fact]
    public void VerifyPassword_Accepts_Correct_Password()
    {
        var (hash, salt) = _service.HashPassword("Sup3r$ecure!");
        _service.VerifyPassword("Sup3r$ecure!", hash, salt).Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_Rejects_Wrong_Password()
    {
        var (hash, salt) = _service.HashPassword("Sup3r$ecure!");
        _service.VerifyPassword("Sup3r$ecure?", hash, salt).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void VerifyPassword_Rejects_Empty_Input(string? candidate)
    {
        var (hash, salt) = _service.HashPassword("Sup3r$ecure!");
        _service.VerifyPassword(candidate!, hash, salt).Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_Rejects_Corrupt_Hash()
    {
        var (_, salt) = _service.HashPassword("Sup3r$ecure!");
        _service.VerifyPassword("Sup3r$ecure!", "not-base-64!!", salt).Should().BeFalse();
    }

    [Theory]
    [InlineData("Sup3r$ecure!", true)]
    [InlineData("Th1s!Works", true)]
    [InlineData("short1!A", true)]      // exactly 8 chars
    [InlineData("nouppercase1!", false)]
    [InlineData("NOLOWERCASE1!", false)]
    [InlineData("NoDigitsHere!", false)]
    [InlineData("NoSymbol12345", false)]
    [InlineData("Aa1!", false)]         // too short
    [InlineData("", false)]
    public void ValidatePasswordStrength_Matches_Rule(string password, bool expected)
    {
        _service.ValidatePasswordStrength(password).Should().Be(expected);
    }
}
