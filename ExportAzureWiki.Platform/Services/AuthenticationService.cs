using ExportAzureWiki.Platform.Backend;
using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Platform.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IAuthenticationBackend _backend;
    private AuthenticatedUser? _currentUser;

    public AuthenticationService()
        : this(new AuthenticationBackend())
    {
    }

    internal AuthenticationService(IAuthenticationBackend backend)
    {
        _backend = backend;
    }

    public bool IsAuthenticated => _currentUser != null;
    public AuthenticatedUser? CurrentUser => _currentUser;

    public async Task<AuthenticationOutcome> AuthenticateLocalAsync(string usernameOrEmail, string password)
    {
        var result = await _backend.AuthenticateLocalAsync(usernameOrEmail, password).ConfigureAwait(false);
        _currentUser = result.Success ? result.User : null;
        return result;
    }

    public async Task<AuthenticationOutcome> AuthenticateAzureAsync()
    {
        var result = await _backend.AuthenticateAzureAsync().ConfigureAwait(false);
        _currentUser = result.Success ? result.User : null;
        return result;
    }

    public async Task SaveCurrentUserPreferredLanguageAsync(string languageCode)
    {
        if (_currentUser == null)
        {
            return;
        }

        if (!int.TryParse(_currentUser.Id, out var userId) || userId <= 0)
        {
            return;
        }

        await _backend.SavePreferredLanguageAsync(userId, languageCode).ConfigureAwait(false);
        _currentUser.PreferredLanguage = languageCode;
    }

    public void SignOut()
    {
        _currentUser = null;
    }
}




