namespace ExportAzureWiki.Core.Authentication;

/// <summary>
/// Rules an admin can tune to match the organization's password baseline.
/// Defaults match OWASP's "balanced" guidance: 8 chars minimum, one
/// upper, one lower, one digit, one symbol. Tighter setups (12+ chars,
/// passphrase-only) are configured by overriding fields on the
/// AuthenticationConfig surface.
/// </summary>
public sealed class PasswordPolicy
{
    public int MinLength { get; init; } = 8;
    public bool RequireUppercase { get; init; } = true;
    public bool RequireLowercase { get; init; } = true;
    public bool RequireDigit { get; init; } = true;
    public bool RequireSymbol { get; init; } = true;

    /// <summary>
    /// Compile-time-known default. Use this when no configured policy is
    /// available (boot, tests, bootstrap admin).
    /// </summary>
    public static PasswordPolicy Default { get; } = new();

    /// <summary>
    /// Evaluates the policy against a candidate password and returns the
    /// first failure as a culture-neutral error key, or null if the
    /// password passes.
    /// </summary>
    public string? FirstViolation(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "password.policy.empty";
        }
        if (password.Length < MinLength)
        {
            return "password.policy.too_short";
        }
        if (RequireUppercase && !password.Any(char.IsUpper))
        {
            return "password.policy.missing_uppercase";
        }
        if (RequireLowercase && !password.Any(char.IsLower))
        {
            return "password.policy.missing_lowercase";
        }
        if (RequireDigit && !password.Any(char.IsDigit))
        {
            return "password.policy.missing_digit";
        }
        if (RequireSymbol && !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            return "password.policy.missing_symbol";
        }
        return null;
    }

    public bool IsSatisfiedBy(string? password) => FirstViolation(password) is null;
}
