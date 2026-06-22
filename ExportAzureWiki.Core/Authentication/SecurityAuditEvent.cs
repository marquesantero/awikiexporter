namespace ExportAzureWiki.Core.Authentication;

/// <summary>
/// Closed set of security-relevant events the platform records to its
/// audit log. Stored as a short string in the database so adding a new
/// category does not require a schema migration.
/// </summary>
public static class SecurityAuditEventTypes
{
    public const string LoginSuccess = "login.success";
    public const string LoginFailure = "login.failure";
    public const string Logout = "logout";
    public const string AccountLocked = "account.locked";
    public const string AccountUnlocked = "account.unlocked";
    public const string PasswordChanged = "password.changed";
    public const string PasswordResetRequested = "password.reset.requested";
    public const string AdminPolicyChanged = "policy.admin.changed";
    public const string PermissionGranted = "permission.granted";
    public const string PermissionRevoked = "permission.revoked";
}

/// <summary>
/// One row of the audit log. Used by both the writer (insert path) and
/// the reader (admin viewer). The Detail column carries a compact JSON
/// blob the writer composes; the reader presents it as-is so new event
/// types do not require a UI rebuild.
/// </summary>
public sealed class SecurityAuditEntry
{
    public long Id { get; init; }
    public DateTime OccurredAt { get; init; }
    public string EventType { get; init; } = string.Empty;

    /// <summary>Numeric user id when known; null for failures against
    /// unknown usernames.</summary>
    public int? UserId { get; init; }

    /// <summary>Username as supplied by the caller (may not match a real
    /// user). Stored verbatim so brute-force attempts against
    /// non-existent accounts are still recoverable from the log.</summary>
    public string? Username { get; init; }

    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }

    /// <summary>Free-form structured detail. JSON when the writer composes
    /// it; plain text is also accepted.</summary>
    public string? Detail { get; init; }
}
