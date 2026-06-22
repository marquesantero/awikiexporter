namespace ExportAzureWiki.Core.Authentication;

public enum AuthProvider
{
    None = 0,
    AzureAD = 1,
    MicrosoftAccount = 2,
    GitHub = 3,
    Google = 4,
    Local = 5
}

public sealed class AuthenticatedUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AuthProvider Provider { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime LastLoginAt { get; set; } = DateTime.Now;
    public string? PreferredLanguage { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
}

public sealed class ExternalProviderUser
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
    public string? ObjectId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
}

public sealed class AuthenticationOutcome
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public AuthenticatedUser? User { get; set; }

    public static AuthenticationOutcome Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };

    public static AuthenticationOutcome Succeeded(AuthenticatedUser user) =>
        new() { Success = true, User = user };
}
