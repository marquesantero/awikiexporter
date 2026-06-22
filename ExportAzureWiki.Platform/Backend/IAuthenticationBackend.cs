using ExportAzureWiki.Core.Authentication;

namespace ExportAzureWiki.Platform.Backend;

internal interface IAuthenticationBackend
{
    Task<AuthenticationOutcome> AuthenticateLocalAsync(string usernameOrEmail, string password);
    Task<AuthenticationOutcome> AuthenticateAzureAsync();
    Task SavePreferredLanguageAsync(int userId, string languageCode);
}





