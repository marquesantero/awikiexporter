using ExportAzureWiki.Core.Authentication;

namespace ExportAzureWiki.Tests.Security;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Sup3r$ecure!", null)]
    [InlineData("Th1s!Works", null)]
    [InlineData("short1!A", null)]
    [InlineData("nouppercase1!", "password.policy.missing_uppercase")]
    [InlineData("NOLOWERCASE1!", "password.policy.missing_lowercase")]
    [InlineData("NoDigitsHere!", "password.policy.missing_digit")]
    [InlineData("NoSymbol12345", "password.policy.missing_symbol")]
    [InlineData("Aa1!", "password.policy.too_short")]
    [InlineData("", "password.policy.empty")]
    [InlineData("   ", "password.policy.empty")]
    public void Default_Policy_Reports_First_Violation(string password, string? expected)
    {
        PasswordPolicy.Default.FirstViolation(password).Should().Be(expected);
    }

    [Fact]
    public void Custom_Policy_Requires_Twelve_Chars()
    {
        var policy = new PasswordPolicy { MinLength = 12 };
        policy.IsSatisfiedBy("Short!1A").Should().BeFalse();
        policy.IsSatisfiedBy("MuchLonger!1A").Should().BeTrue();
    }

    [Fact]
    public void Custom_Policy_Can_Disable_Symbol_Requirement()
    {
        var policy = new PasswordPolicy { RequireSymbol = false };
        policy.IsSatisfiedBy("NoSymbol12345").Should().BeTrue();
    }

    [Fact]
    public void Custom_Policy_Can_Disable_All_Character_Class_Requirements()
    {
        // Passphrase style: just length.
        var policy = new PasswordPolicy
        {
            MinLength = 20,
            RequireUppercase = false,
            RequireLowercase = false,
            RequireDigit = false,
            RequireSymbol = false,
        };

        policy.IsSatisfiedBy("correct horse battery staple").Should().BeTrue();
        policy.IsSatisfiedBy("too short").Should().BeFalse();
    }

    [Fact]
    public void Default_Singleton_Is_Stable()
    {
        // Defaults are used in many places; guard against accidental drift.
        var d = PasswordPolicy.Default;
        d.MinLength.Should().Be(8);
        d.RequireUppercase.Should().BeTrue();
        d.RequireLowercase.Should().BeTrue();
        d.RequireDigit.Should().BeTrue();
        d.RequireSymbol.Should().BeTrue();
    }
}
