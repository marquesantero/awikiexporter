namespace ExportAzureWiki.Core.Authentication;

public sealed record LocalUserSnapshot(
    int Id,
    string Username,
    string Email,
    string? DisplayName,
    string? PasswordHash,
    string? PasswordSalt,
    bool IsActive);

public sealed record LocalAuthenticationDecision(
    bool Success,
    string? ErrorKey)
{
    public static LocalAuthenticationDecision Passed() => new(true, null);
    public static LocalAuthenticationDecision Failed(string errorKey) => new(false, errorKey);
}

public static class LocalAuthenticationRules
{
    public const string ErrorUsernamePasswordRequired = "auth.error.username_password_required";
    public const string ErrorInvalidUsernamePassword = "auth.error.invalid_username_password";
    public const string ErrorUserInactive = "auth.error.user_inactive";

    public static LocalAuthenticationDecision Evaluate(
        string? usernameOrEmail,
        string? password,
        LocalUserSnapshot? user,
        Func<string, string, string, bool> verifyPassword)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
        {
            return LocalAuthenticationDecision.Failed(ErrorUsernamePasswordRequired);
        }

        if (user == null)
        {
            return LocalAuthenticationDecision.Failed(ErrorInvalidUsernamePassword);
        }

        if (!user.IsActive)
        {
            return LocalAuthenticationDecision.Failed(ErrorUserInactive);
        }

        var hash = user.PasswordHash ?? string.Empty;
        var salt = user.PasswordSalt ?? string.Empty;
        var normalizedPassword = password.Trim();
        var validPassword =
            verifyPassword(password, hash, salt) ||
            (!string.Equals(password, normalizedPassword, StringComparison.Ordinal) &&
             verifyPassword(normalizedPassword, hash, salt));

        if (!validPassword)
        {
            return LocalAuthenticationDecision.Failed(ErrorInvalidUsernamePassword);
        }

        return LocalAuthenticationDecision.Passed();
    }
}
