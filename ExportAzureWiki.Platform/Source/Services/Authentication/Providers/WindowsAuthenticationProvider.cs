using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using ExportAzureWiki.Data;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models;
using ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Services.Authentication.Providers;

/// <summary>
/// Windows/Active Directory authentication provider
/// </summary>
public class WindowsAuthenticationProvider : IAuthMethodProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthenticationConfigService _configService;

    public AuthenticationMethod Method => AuthenticationMethod.Windows;
    public string DisplayName => "Windows/Active Directory";

    public WindowsAuthenticationProvider(
        IUnitOfWork unitOfWork,
        AuthenticationConfigService configService)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public async Task<AuthMethodResult> AuthenticateAsync(string username, string password)
    {
        try
        {
            // Check if Windows authentication is allowed
            if (!await _configService.IsMethodAllowedAsync(AuthenticationMethod.Windows).ConfigureAwait(false))
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.windows.not_enabled"));
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.username_password_required"));
            }

            // Parse username (could be in format DOMAIN\username or username@domain)
            var parsedUsername = ParseUsername(username, out var domain);

            // Validate credentials against Active Directory
            bool isValid = ValidateCredentials(domain, parsedUsername, password, out var userPrincipal);

            if (!isValid || userPrincipal == null)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.windows.invalid_credentials"));
            }

            // Get user groups from Windows/AD
            var groups = new List<string>();
            if (await _configService.UseWindowsPermissionsAsync().ConfigureAwait(false))
            {
                groups = GetUserGroups(userPrincipal);
            }

            // Get registered user in database and update profile/group sync
            var user = await GetRegisteredUserAsync(userPrincipal, groups).ConfigureAwait(false);

            if (user == null)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.external_user_not_registered"));
            }

            userPrincipal.Dispose();

            return AuthMethodResult.Succeeded(user, groups);
        }
        catch (Exception ex)
        {
            return AuthMethodResult.Failed(LocalizationManager.Sf("auth.error.authenticate", ex.Message));
        }
    }

    public async Task<AuthMethodResult> AuthenticateWindowsAsync()
    {
        try
        {
            // Check if Windows authentication is allowed
            if (!await _configService.IsMethodAllowedAsync(AuthenticationMethod.Windows).ConfigureAwait(false))
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.windows.not_enabled"));
            }

            // Get current Windows identity
            var identity = WindowsIdentity.GetCurrent();
            if (identity == null || !identity.IsAuthenticated)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.windows.user_not_authenticated"));
            }

            // Get user principal from current identity
            var userPrincipal = UserPrincipal.FindByIdentity(
                new PrincipalContext(ContextType.Domain),
                IdentityType.Sid,
                identity.User?.Value);

            if (userPrincipal == null)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.windows.user_info_unavailable"));
            }

            // Get user groups from Windows/AD
            var groups = new List<string>();
            if (await _configService.UseWindowsPermissionsAsync().ConfigureAwait(false))
            {
                groups = GetUserGroups(userPrincipal);
            }

            // Get registered user in database and update profile/group sync
            var user = await GetRegisteredUserAsync(userPrincipal, groups).ConfigureAwait(false);

            if (user == null)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.external_user_not_registered"));
            }

            userPrincipal.Dispose();

            return AuthMethodResult.Succeeded(user, groups);
        }
        catch (Exception ex)
        {
            return AuthMethodResult.Failed(LocalizationManager.Sf("auth.error.authenticate", ex.Message));
        }
    }

    public async Task<bool> IsConfiguredAsync()
    {
        return await _configService.IsMethodAllowedAsync(AuthenticationMethod.Windows).ConfigureAwait(false);
    }

    private string ParseUsername(string username, out string? domain)
    {
        domain = null;

        // Check for DOMAIN\username format
        if (username.Contains('\\'))
        {
            var parts = username.Split('\\');
            if (parts.Length == 2)
            {
                domain = parts[0];
                return parts[1];
            }
        }

        // Check for username@domain format
        if (username.Contains('@'))
        {
            var parts = username.Split('@');
            if (parts.Length == 2)
            {
                domain = parts[1];
                return parts[0];
            }
        }

        return username;
    }

    private bool ValidateCredentials(string? domain, string username, string password, out UserPrincipal? userPrincipal)
    {
        userPrincipal = null;

        try
        {
            // Create principal context
            var contextType = string.IsNullOrWhiteSpace(domain) ? ContextType.Machine : ContextType.Domain;
            var context = string.IsNullOrWhiteSpace(domain)
                ? new PrincipalContext(contextType)
                : new PrincipalContext(contextType, domain);

            // Validate credentials
            bool isValid = context.ValidateCredentials(username, password);

            if (isValid)
            {
                // Get user principal
                userPrincipal = UserPrincipal.FindByIdentity(context, username);
            }

            return isValid;
        }
        catch
        {
            return false;
        }
    }

    private List<string> GetUserGroups(UserPrincipal userPrincipal)
    {
        var groups = new List<string>();

        try
        {
            var principalGroups = userPrincipal.GetAuthorizationGroups();
            foreach (var group in principalGroups)
            {
                if (group is GroupPrincipal groupPrincipal)
                {
                    groups.Add(groupPrincipal.Name);
                }
                group.Dispose();
            }
        }
        catch
        {
            // If we can't get groups, continue with empty list
        }

        return groups;
    }

    private async Task<User?> GetRegisteredUserAsync(UserPrincipal userPrincipal, List<string> groups)
    {
        try
        {
            var config = await _configService.GetConfigurationAsync().ConfigureAwait(false);

            // Try to find existing user by Windows SID or username
            var user = await _unitOfWork.Users.GetByUsernameAsync(userPrincipal.SamAccountName).ConfigureAwait(false);

            if (user == null)
            {
                return null;
            }

            // Update last login
            user.LastLoginAt = DateTime.Now;
            await _unitOfWork.Users.UpdateAsync(user).ConfigureAwait(false);

            // Sync groups if enabled
            if (config.SyncWindowsGroups)
            {
                await SyncUserGroupsAsync(user.Id, groups).ConfigureAwait(false);
            }

            return user;
        }
        catch
        {
            return null;
        }
    }

    private async Task SyncUserGroupsAsync(int userId, List<string> windowsGroups)
    {
        try
        {
            // Get existing groups for user
            var existingGroups = await _unitOfWork.Groups.GetByUserIdAsync(userId).ConfigureAwait(false);
            var existingGroupNames = existingGroups.Select(g => g.Name).ToList();

            // Add user to new groups
            foreach (var groupName in windowsGroups)
            {
                if (!existingGroupNames.Contains(groupName))
                {
                    // Find or create group
                    var group = await _unitOfWork.Groups.GetByNameAsync(groupName).ConfigureAwait(false);
                    if (group == null)
                    {
                        group = new IdentityGroup
                        {
                            Name = groupName,
                            Description = $"Grupo sincronizado do Windows/AD: {groupName}",
                            IsSystem = false,
                            Source = "Windows",
                            CreatedAt = DateTime.Now
                        };
                        var groupId = await _unitOfWork.Groups.AddAsync(group).ConfigureAwait(false);
                        group.Id = groupId;
                    }

                    // Add user to group
                    await _unitOfWork.Groups.AddUserAsync(userId, group.Id).ConfigureAwait(false);
                }
            }

            // Remove user from groups that are no longer in Windows/AD
            foreach (var existingGroup in existingGroups)
            {
                if (existingGroup.Source == "Windows" && !windowsGroups.Contains(existingGroup.Name))
                {
                    await _unitOfWork.Groups.RemoveUserAsync(userId, existingGroup.Id).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // If sync fails, continue without group sync
        }
    }
}
