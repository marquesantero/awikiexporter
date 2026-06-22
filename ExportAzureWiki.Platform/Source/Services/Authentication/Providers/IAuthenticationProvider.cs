using ExportAzureWiki.Models;
using ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Services.Authentication.Providers;

/// <summary>
/// Interface for authentication method providers
/// </summary>
public interface IAuthMethodProvider
{
    /// <summary>
    /// Gets the authentication method supported by this provider
    /// </summary>
    AuthenticationMethod Method { get; }

    /// <summary>
    /// Gets the provider display name
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Authenticates a user with the given credentials
    /// </summary>
    /// <param name="username">Username or email</param>
    /// <param name="password">Password or authentication token</param>
    /// <returns>AuthMethodResult with user information if successful</returns>
    Task<AuthMethodResult> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Authenticates a user using Windows/integrated authentication
    /// </summary>
    /// <returns>AuthMethodResult with user information if successful</returns>
    Task<AuthMethodResult> AuthenticateWindowsAsync();

    /// <summary>
    /// Validates if the provider is properly configured
    /// </summary>
    /// <returns>True if the provider is ready to use</returns>
    Task<bool> IsConfiguredAsync();
}

/// <summary>
/// Result of an authentication method attempt
/// </summary>
public class AuthMethodResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public User? User { get; set; }
    public List<string>? Groups { get; set; }
    public Dictionary<string, string>? AdditionalData { get; set; }

    public static AuthMethodResult Succeeded(User user, List<string>? groups = null)
    {
        return new AuthMethodResult
        {
            Success = true,
            User = user,
            Groups = groups ?? new List<string>(),
            AdditionalData = new Dictionary<string, string>()
        };
    }

    public static AuthMethodResult Failed(string errorMessage)
    {
        return new AuthMethodResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
