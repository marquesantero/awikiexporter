using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ExportAzureWiki.Data;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models;
using ExportAzureWiki.Models.Entities;
using Serilog;

namespace ExportAzureWiki.Services.Authentication.Providers;

/// <summary>
/// Azure Active Directory authentication provider
/// </summary>
public class AzureADAuthenticationProvider : IAuthMethodProvider
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthenticationConfigService _configService;
    private readonly HttpClient _httpClient;

    public AuthenticationMethod Method => AuthenticationMethod.AzureAD;
    public string DisplayName => "Azure Active Directory";

    public AzureADAuthenticationProvider(
        IUnitOfWork unitOfWork,
        AuthenticationConfigService configService,
        HttpClient httpClient)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<AuthMethodResult> AuthenticateAsync(string username, string password)
    {
        // Azure AD authentication is typically done via OAuth flow, not username/password
        // This method is for compatibility but should redirect to OAuth flow
        return await Task.FromResult(AuthMethodResult.Failed(
            LocalizationManager.S("auth.azuread.oauth_only"))).ConfigureAwait(false);
    }

    public async Task<AuthMethodResult> AuthenticateWindowsAsync()
    {
        // Windows authentication is not applicable for Azure AD
        return await Task.FromResult(AuthMethodResult.Failed(
            LocalizationManager.S("auth.azuread.windows_not_applicable"))).ConfigureAwait(false);
    }

    /// <summary>
    /// Authenticates using an Azure AD access token
    /// </summary>
    public async Task<AuthMethodResult> AuthenticateWithTokenAsync(string accessToken)
    {
        try
        {
            // Check if Azure AD authentication is allowed
            if (!await _configService.IsMethodAllowedAsync(AuthenticationMethod.AzureAD).ConfigureAwait(false))
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.azuread.not_enabled"));
            }

            // Get user info from Microsoft Graph
            var userInfo = await GetUserInfoFromGraphAsync(accessToken).ConfigureAwait(false);
            if (userInfo == null)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.azuread.user_info_unavailable"));
            }

            // Get user groups if enabled
            var groups = new List<string>();
            if (await _configService.UseAzureADPermissionsAsync().ConfigureAwait(false))
            {
                groups = await GetUserGroupsFromGraphAsync(accessToken).ConfigureAwait(false);
            }

            // Get registered user in database and update profile/group sync
            var user = await GetRegisteredUserAsync(userInfo, groups).ConfigureAwait(false);

            if (user == null)
            {
                return AuthMethodResult.Failed(LocalizationManager.S("auth.error.external_user_not_registered"));
            }

            return AuthMethodResult.Succeeded(user, groups);
        }
        catch (Exception ex)
        {
            return AuthMethodResult.Failed(LocalizationManager.Sf("auth.error.authenticate", ex.Message));
        }
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var config = await _configService.GetConfigurationAsync().ConfigureAwait(false);
        return await _configService.IsMethodAllowedAsync(AuthenticationMethod.AzureAD).ConfigureAwait(false) &&
               !string.IsNullOrWhiteSpace(config.AzureADTenantId);
    }

    private async Task<AzureADUserInfo?> GetUserInfoFromGraphAsync(string accessToken)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.GetAsync("https://graph.microsoft.com/v1.0/me").ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var userInfo = JsonSerializer.Deserialize<AzureADUserInfo>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return userInfo;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Azure AD Graph /me lookup failed");
            return null;
        }
    }

    private async Task<List<string>> GetUserGroupsFromGraphAsync(string accessToken)
    {
        var groups = new List<string>();

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _httpClient.GetAsync("https://graph.microsoft.com/v1.0/me/memberOf").ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return groups;
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var graphResponse = JsonSerializer.Deserialize<GraphGroupsResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (graphResponse?.Value != null)
            {
                groups = graphResponse.Value
                    .Where(g => !string.IsNullOrWhiteSpace(g.DisplayName))
                    .Select(g => g.DisplayName!)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Azure AD Graph /me/memberOf lookup failed; continuing with empty groups");
        }

        return groups;
    }

    private async Task<User?> GetRegisteredUserAsync(AzureADUserInfo userInfo, List<string> groups)
    {
        try
        {
            var config = await _configService.GetConfigurationAsync().ConfigureAwait(false);

            // Try to find existing user by Azure AD ID or email
            var user = await _unitOfWork.Users.GetByExternalIdAsync(userInfo.Id).ConfigureAwait(false);
            if (user == null)
            {
                user = await _unitOfWork.Users.GetByEmailAsync(userInfo.Mail ?? userInfo.UserPrincipalName).ConfigureAwait(false);
            }

            if (user == null)
            {
                return null;
            }

            // Update user information
            user.DisplayName = userInfo.DisplayName ?? user.DisplayName;
            user.Email = userInfo.Mail ?? user.Email;
            user.LastLoginAt = DateTime.Now;
            await _unitOfWork.Users.UpdateAsync(user).ConfigureAwait(false);

            // Sync groups if enabled
            if (config.SyncAzureADGroups)
            {
                await SyncUserGroupsAsync(user.Id, groups).ConfigureAwait(false);
            }

            return user;
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Failed to resolve registered Azure AD user {ExternalId}",
                userInfo?.Id);
            return null;
        }
    }

    private async Task SyncUserGroupsAsync(int userId, List<string> azureGroups)
    {
        try
        {
            // Get existing groups for user
            var existingGroups = await _unitOfWork.Groups.GetByUserIdAsync(userId).ConfigureAwait(false);
            var existingGroupNames = existingGroups.Select(g => g.Name).ToList();

            // Add user to new groups
            foreach (var groupName in azureGroups)
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
                            Description = $"Grupo sincronizado do Azure AD: {groupName}",
                            IsSystem = false,
                            Source = "AzureAD",
                            CreatedAt = DateTime.Now
                        };
                        var groupId = await _unitOfWork.Groups.AddAsync(group).ConfigureAwait(false);
                        group.Id = groupId;
                    }

                    // Add user to group
                    await _unitOfWork.Groups.AddUserAsync(userId, group.Id).ConfigureAwait(false);
                }
            }

            // Remove user from groups that are no longer in Azure AD
            foreach (var existingGroup in existingGroups)
            {
                if (existingGroup.Source == "AzureAD" && !azureGroups.Contains(existingGroup.Name))
                {
                    await _unitOfWork.Groups.RemoveUserAsync(userId, existingGroup.Id).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Azure AD group sync failed for user {UserId}; existing group membership left untouched",
                userId);
        }
    }
}

/// <summary>
/// Azure AD user information from Microsoft Graph
/// </summary>
internal class AzureADUserInfo
{
    public string Id { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Mail { get; set; }
    public string? GivenName { get; set; }
    public string? Surname { get; set; }
}

/// <summary>
/// Microsoft Graph groups response
/// </summary>
internal class GraphGroupsResponse
{
    public List<GraphGroup>? Value { get; set; }
}

/// <summary>
/// Microsoft Graph group
/// </summary>
internal class GraphGroup
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
}
