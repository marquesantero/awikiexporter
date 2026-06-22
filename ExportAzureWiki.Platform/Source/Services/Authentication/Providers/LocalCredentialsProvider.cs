using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Data;
using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Models;
using ExportAzureWiki.Models.Authentication;
using ExportAzureWiki.Services.Authentication;
using ExportAzureWiki.Localization;
using Dapper;
using Serilog;

namespace ExportAzureWiki.Services.Authentication.Providers;

/// <summary>
/// Provides local username/password authentication in the provider pipeline.
/// Implements brute-force protection: failed logins increment a per-account
/// counter, and the account is locked for a configurable cool-down period
/// once the threshold is reached. The clock is local time (DateTime.Now)
/// to match every other timestamp the schema stores.
/// </summary>
public sealed class LocalCredentialsProvider : IAuthenticationProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly PasswordHashingService _passwordHashingService;
    private readonly AuthenticationConfigService _configService;
    private readonly AuthenticationConfig _runtimeConfig;
    private readonly SecurityAuditService? _audit;

    public AuthenticationProvider ProviderType => AuthenticationProvider.Local;
    public string ProviderName => "Sistema Local";

    public LocalCredentialsProvider(
        IUnitOfWork unitOfWork,
        PasswordHashingService passwordHashingService,
        AuthenticationConfigService configService,
        AuthenticationConfig? runtimeConfig = null,
        SecurityAuditService? audit = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _passwordHashingService = passwordHashingService ?? throw new ArgumentNullException(nameof(passwordHashingService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _runtimeConfig = runtimeConfig ?? new AuthenticationConfig();
        _audit = audit;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(Dictionary<string, string>? parameters = null)
    {
        if (!await _configService.IsMethodAllowedAsync(AuthenticationMethod.Local).ConfigureAwait(false))
        {
            return Fail("auth.local.not_enabled");
        }

        var username = parameters != null && parameters.TryGetValue("username", out var u) ? u : string.Empty;
        var password = parameters != null && parameters.TryGetValue("password", out var p) ? p : string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            // The UI shell (WPF LoginViewModel, CLI prompt) is responsible
            // for collecting credentials. Platform no longer pops its own
            // WinForms dialog. See Fase 3.1 / 4.x.
            return Fail("auth.error.username_password_required");
        }

        var user = await _unitOfWork.Users.GetByUsernameAsync(username).ConfigureAwait(false)
                   ?? await _unitOfWork.Users.GetByEmailAsync(username).ConfigureAwait(false);

        if (user == null)
        {
            // Constant-time-ish behavior: still call the hashing service
            // so failure-against-unknown and failure-against-known take a
            // comparable amount of CPU. PBKDF2 keeps it cheap-but-stable.
            _passwordHashingService.VerifyPassword(password, "AAAAAAAAAA==", "AAAAAAAAAA==");
            await AuditAsync(SecurityAuditEventTypes.LoginFailure, null, username, new { reason = "unknown_user" }).ConfigureAwait(false);
            return Fail("auth.error.invalid_username_password");
        }

        if (!user.IsActive)
        {
            await AuditAsync(SecurityAuditEventTypes.LoginFailure, user.Id, username, new { reason = "inactive" }).ConfigureAwait(false);
            return Fail("auth.error.user_inactive");
        }

        if (IsLockedOut(user, out var lockedUntil))
        {
            Log.Warning(
                "Local login rejected: account {UserId} is locked until {LockedUntil:o}",
                user.Id, lockedUntil);
            await AuditAsync(SecurityAuditEventTypes.LoginFailure, user.Id, username, new { reason = "locked", lockedUntil }).ConfigureAwait(false);
            return Fail("auth.error.account_locked");
        }

        var normalizedPassword = password.Trim();
        var validPassword =
            _passwordHashingService.VerifyPassword(password, user.PasswordHash ?? string.Empty, user.PasswordSalt ?? string.Empty) ||
            (!string.Equals(password, normalizedPassword, StringComparison.Ordinal) &&
             _passwordHashingService.VerifyPassword(normalizedPassword, user.PasswordHash ?? string.Empty, user.PasswordSalt ?? string.Empty));

        if (!validPassword)
        {
            await RecordFailureAsync(user).ConfigureAwait(false);
            await AuditAsync(SecurityAuditEventTypes.LoginFailure, user.Id, username, new
            {
                reason = "wrong_password",
                failedCount = user.FailedLoginCount,
                lockedUntil = user.LockedUntil,
            }).ConfigureAwait(false);
            return Fail("auth.error.invalid_username_password");
        }

        await RecordSuccessAsync(user).ConfigureAwait(false);
        await AuditAsync(SecurityAuditEventTypes.LoginSuccess, user.Id, username).ConfigureAwait(false);

        var effectiveIsAdmin = await ResolveEffectiveIsAdminAsync(user.Id).ConfigureAwait(false);

        var authenticatedUser = new User
        {
            Id = user.Id.ToString(),
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName ?? user.Username,
            Provider = AuthenticationProvider.Local,
            ProviderId = user.Id.ToString(),
            IsActive = user.IsActive,
            LastLoginAt = DateTime.Now,
            Roles = effectiveIsAdmin ? new List<string> { "Admin" } : new List<string>()
        };

        return new AuthenticationResult
        {
            Success = true,
            User = authenticatedUser
        };
    }

    public Task<bool> ValidateTokenAsync(string token) => Task.FromResult(true);
    public Task<User?> GetUserInfoAsync(string token) => Task.FromResult<User?>(null);
    public Task<string?> RefreshTokenAsync(string refreshToken) => Task.FromResult<string?>(null);
    public Task SignOutAsync(User user) => Task.CompletedTask;

    public bool IsConfigured()
    {
        try
        {
            return _configService.IsMethodAllowedAsync(AuthenticationMethod.Local).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LocalCredentialsProvider.IsConfigured probe failed");
            return false;
        }
    }

    private bool IsLockedOut(Models.Entities.User user, out DateTime lockedUntil)
    {
        lockedUntil = user.LockedUntil ?? DateTime.MinValue;
        if (user.LockedUntil is { } until && until > DateTime.Now)
        {
            return true;
        }
        return false;
    }

    private async Task RecordFailureAsync(Models.Entities.User user)
    {
        user.FailedLoginCount += 1;

        var wasLockedThisAttempt = false;
        if (_runtimeConfig.MaxFailedAttempts > 0 &&
            user.FailedLoginCount >= _runtimeConfig.MaxFailedAttempts)
        {
            var duration = Math.Max(1, _runtimeConfig.LockoutDurationMinutes);
            user.LockedUntil = DateTime.Now.AddMinutes(duration);
            Log.Warning(
                "Local login locked account {UserId} for {LockoutMinutes} min after {Failures} failed attempts",
                user.Id, duration, user.FailedLoginCount);
            wasLockedThisAttempt = true;
        }

        user.LastModifiedAt = DateTime.Now;
        await _unitOfWork.Users.UpdateAsync(user).ConfigureAwait(false);

        if (wasLockedThisAttempt)
        {
            await AuditAsync(SecurityAuditEventTypes.AccountLocked, user.Id, user.Username, new
            {
                user.FailedLoginCount,
                lockedUntil = user.LockedUntil,
            }).ConfigureAwait(false);
        }
    }

    private Task AuditAsync(string eventType, int? userId, string? username, object? detail = null)
    {
        return _audit?.RecordAsync(eventType, userId, username, detail) ?? Task.CompletedTask;
    }

    private async Task RecordSuccessAsync(Models.Entities.User user)
    {
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.Now;
        user.LastModifiedAt = DateTime.Now;
        await _unitOfWork.Users.UpdateAsync(user).ConfigureAwait(false);
    }

    private static AuthenticationResult Fail(string messageKey) => new()
    {
        Success = false,
        ErrorMessage = LocalizationManager.S(messageKey, messageKey)
    };

    private static async Task<bool> ResolveEffectiveIsAdminAsync(int userId)
    {
        try
        {
            var factory = new DbConnectionFactory();
            using var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
            var policiesTable = factory.GetDatabaseType() == DatabaseType.SqlServer
                ? "[dbo].[AccessPolicies]"
                : "access_policies";

            var sql = $"""
                       SELECT COUNT(1)
                       FROM {policiesTable}
                       WHERE identity_type = @IdentityType
                         AND identity_id = @IdentityId
                         AND is_admin = @IsAdmin
                         AND is_active = @IsActive
                       """;

            var count = await connection.QuerySingleAsync<int>(sql, new
            {
                IdentityType = 0, // AccessPolicyIdentityType.User
                IdentityId = userId.ToString(),
                IsAdmin = true,
                IsActive = true
            }).ConfigureAwait(false);

            return count > 0;
        }
        catch (Exception ex)
        {
            // Same fail-closed rule as the other admin-resolution sites:
            // default to NOT-admin and log explicitly so an operator can
            // notice the underlying DB issue.
            Log.Error(ex,
                "ResolveEffectiveIsAdminAsync failed for user {UserId}; defaulting to non-admin",
                userId);
            return false;
        }
    }
}
