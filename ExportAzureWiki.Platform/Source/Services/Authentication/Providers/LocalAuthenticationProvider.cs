using ExportAzureWiki.Data;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models;
using ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Services.Authentication.Providers;

/// <summary>
/// Local authentication provider using database-stored users and passwords
/// </summary>
public class LocalAuthenticationProvider : IAuthMethodProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly PasswordHashingService _passwordHashingService;
    private readonly AuthenticationConfigService _configService;

    public AuthenticationMethod Method => AuthenticationMethod.Local;
    public string DisplayName => "Sistema Local (Usuário e Senha)";

    public LocalAuthenticationProvider(
        IUnitOfWork unitOfWork,
        PasswordHashingService passwordHashingService,
        AuthenticationConfigService configService)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _passwordHashingService = passwordHashingService ?? throw new ArgumentNullException(nameof(passwordHashingService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public async Task<AuthMethodResult> AuthenticateAsync(string username, string password)
    {
        try
        {
            // Check if local authentication is allowed
            if (!await _configService.IsMethodAllowedAsync(AuthenticationMethod.Local).ConfigureAwait(false))
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.local.not_enabled"));
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.username_password_required"));
            }

            // Find user by username or email
            var user = await _unitOfWork.Users.GetByUsernameAsync(username).ConfigureAwait(false);
            if (user == null)
            {
                user = await _unitOfWork.Users.GetByEmailAsync(username).ConfigureAwait(false);
            }

            if (user == null)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.invalid_username_password"));
            }

            // Check if user is active
            if (!user.IsActive)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.user_inactive"));
            }

            // Verify password
            if (!_passwordHashingService.VerifyPassword(password, user.PasswordHash ?? string.Empty, user.PasswordSalt ?? string.Empty))
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.invalid_username_password"));
            }

            // Update last login
            user.LastLoginAt = DateTime.Now;
            await _unitOfWork.Users.UpdateAsync(user).ConfigureAwait(false);

            // Get user groups (if using local permissions)
            var groups = new List<string>();
            if (await _configService.UseLocalPermissionsAsync().ConfigureAwait(false))
            {
                var userGroups = await _unitOfWork.Groups.GetByUserIdAsync(user.Id).ConfigureAwait(false);
                groups = userGroups.Select(g => g.Name).ToList();
            }

            return AuthMethodResult.Succeeded(user, groups);
        }
        catch (Exception ex)
        {
            return AuthMethodResult.Failed(LocalizationManager.Sf("auth.error.authenticate", ex.Message));
        }
    }

    public Task<AuthMethodResult> AuthenticateWindowsAsync()
    {
        // Local provider doesn't support Windows authentication
        return Task.FromResult(AuthMethodResult.Failed(LocalizationManager.S("auth.local.windows_not_supported")));
    }

    public async Task<bool> IsConfiguredAsync()
    {
        return await _configService.IsMethodAllowedAsync(AuthenticationMethod.Local).ConfigureAwait(false);
    }
}
