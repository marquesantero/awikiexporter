using ExportAzureWiki.Models.Authentication;

namespace ExportAzureWiki.Interfaces
{
    public interface IAuthenticationProvider
    {
        AuthenticationProvider ProviderType { get; }
        string ProviderName { get; }
        Task<AuthenticationResult> AuthenticateAsync(Dictionary<string, string>? parameters = null);
        Task<bool> ValidateTokenAsync(string token);
        Task<User?> GetUserInfoAsync(string token);
        Task<string?> RefreshTokenAsync(string refreshToken);
        Task SignOutAsync(User user);
        bool IsConfigured();
    }

    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public User? User { get; set; }
        public string? ErrorMessage { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public class AuthenticationConfig
    {
        public Dictionary<AuthenticationProvider, ProviderConfig> Providers { get; set; } = new();
        public bool AllowMultipleProviders { get; set; } = true;
        public bool RequireAuthentication { get; set; } = true;

        /// <summary>
        /// Absolute session lifetime. After this many minutes from login the
        /// session is rejected even if the user has been active. Defaults to
        /// 24 hours.
        /// </summary>
        public int SessionTimeoutMinutes { get; set; } = 1440;

        /// <summary>
        /// Sliding idle timeout. If the session has not been validated within
        /// this window it is rejected and the user must reauthenticate. Set to
        /// 0 to disable the idle check (absolute expiry only). Defaults to
        /// 60 minutes, which mirrors common corporate session policies.
        /// </summary>
        public int IdleTimeoutMinutes { get; set; } = 60;

        public bool EnableRememberMe { get; set; } = true;

        // Password policy --------------------------------------------------

        public int PasswordMinLength { get; set; } = 8;
        public bool PasswordRequireUppercase { get; set; } = true;
        public bool PasswordRequireLowercase { get; set; } = true;
        public bool PasswordRequireDigit { get; set; } = true;
        public bool PasswordRequireSymbol { get; set; } = true;

        public ExportAzureWiki.Core.Authentication.PasswordPolicy GetPasswordPolicy() => new()
        {
            MinLength = PasswordMinLength,
            RequireUppercase = PasswordRequireUppercase,
            RequireLowercase = PasswordRequireLowercase,
            RequireDigit = PasswordRequireDigit,
            RequireSymbol = PasswordRequireSymbol,
        };

        // Lockout / brute-force protection --------------------------------

        /// <summary>
        /// Maximum number of consecutive failed logins before the account
        /// is locked. Set to 0 to disable lockout entirely. Defaults to 5,
        /// matching OWASP guidance.
        /// </summary>
        public int MaxFailedAttempts { get; set; } = 5;

        /// <summary>
        /// How long an account stays locked after exceeding
        /// <see cref="MaxFailedAttempts"/>. Defaults to 15 minutes -- long
        /// enough to neutralize an online brute-force attempt without
        /// permanently locking out a user who typed wrong.
        /// </summary>
        public int LockoutDurationMinutes { get; set; } = 15;
    }

    public class ProviderConfig
    {
        public bool Enabled { get; set; }
        public Dictionary<string, string> Settings { get; set; } = new();
    }
}
