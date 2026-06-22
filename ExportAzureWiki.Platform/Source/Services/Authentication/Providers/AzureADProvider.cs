using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models.Authentication;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net.Http;
using System.Net.Http.Headers;
using AppAuthenticationResult = ExportAzureWiki.Interfaces.AuthenticationResult;
using MsalAuthenticationResult = Microsoft.Identity.Client.AuthenticationResult;

namespace ExportAzureWiki.Services.Authentication.Providers
{
    /// <summary>
    /// Azure Active Directory provider built on MSAL.NET Public Client.
    ///
    /// Security notes (Fase 4.5):
    /// - PKCE is enabled automatically by MSAL.NET for every Public Client
    ///   interactive flow. It cannot be turned off here, so the standard
    ///   redirect-back exchange is always tied to a code_verifier the
    ///   library generated on the device.
    /// - Refresh tokens are not handled by application code: MSAL keeps
    ///   them in the user token cache. Rotation happens automatically on
    ///   every AcquireTokenSilent call.
    /// - The token cache is persisted to %LocalAppData% and protected by
    ///   DPAPI (CurrentUser scope) through Microsoft.Identity.Client.
    ///   Extensions.Msal. That way sessions survive process restart
    ///   without leaving plaintext tokens on disk and without sharing
    ///   them between Windows users on the same machine.
    /// </summary>
    public class AzureADProvider : BaseAuthenticationProvider
    {
        private static readonly HttpClient _httpClient = new();
        private const string GraphEndpoint = "https://graph.microsoft.com/v1.0";
        private const string CacheFileName = "ExportAzureWiki.msal.cache";
        private const string CacheDirectoryName = "MsalCache";

        private static readonly string[] DefaultScopes =
        {
            "openid",
            "profile",
            "email",
            "User.Read",
            "GroupMember.Read.All"
        };

        private readonly string[] _scopes;
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private IPublicClientApplication? _msalApp;
        private MsalCacheHelper? _cacheHelper;

        public override AuthenticationProvider ProviderType => AuthenticationProvider.AzureAD;
        public override string ProviderName => "Azure Active Directory";

        public AzureADProvider(Dictionary<string, string> config) : base(config)
        {
            _scopes = ParseScopes(GetConfigValue("Scopes"));
        }

        public override bool IsConfigured()
        {
            return !string.IsNullOrEmpty(GetConfigValue("ClientId")) &&
                   !string.IsNullOrEmpty(GetConfigValue("TenantId"));
        }

        public override async Task<AppAuthenticationResult> AuthenticateAsync(Dictionary<string, string>? parameters = null)
        {
            try
            {
                if (!IsConfigured())
                {
                    return new AppAuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.provider.azuread.not_configured")
                    };
                }

                var msalApp = await GetMsalAppAsync().ConfigureAwait(false);
                var accounts = await msalApp.GetAccountsAsync().ConfigureAwait(false);

                try
                {
                    if (accounts.Any())
                    {
                        var silentResult = await msalApp
                            .AcquireTokenSilent(_scopes, accounts.First())
                            .ExecuteAsync().ConfigureAwait(false);

                        return await BuildAuthenticationResultAsync(silentResult).ConfigureAwait(false);
                    }
                }
                catch (MsalUiRequiredException)
                {
                    // Interactive flow required when the cache has no
                    // valid token. Falls through to AcquireTokenInteractive.
                }

                var interactiveResult = await msalApp
                    .AcquireTokenInteractive(_scopes)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync().ConfigureAwait(false);

                return await BuildAuthenticationResultAsync(interactiveResult).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Azure AD interactive authentication failed");
                return new AppAuthenticationResult
                {
                    Success = false,
                    ErrorMessage = LocalizationManager.Sf("auth.provider.azuread.auth_failed", ex.Message)
                };
            }
        }

        public override async Task<bool> ValidateTokenAsync(string token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{GraphEndpoint}/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Azure AD token validation request failed");
                return false;
            }
        }

        public override async Task<User?> GetUserInfoAsync(string token)
        {
            try
            {
                using var meRequest = new HttpRequestMessage(HttpMethod.Get, $"{GraphEndpoint}/me");
                meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var meResponse = await _httpClient.SendAsync(meRequest).ConfigureAwait(false);

                if (!meResponse.IsSuccessStatusCode)
                {
                    Log.Warning(
                        "Azure AD /me returned non-success status {StatusCode}",
                        (int)meResponse.StatusCode);
                    return null;
                }

                var meJson = await meResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var meData = JObject.Parse(meJson);

                var user = new User
                {
                    Provider = AuthenticationProvider.AzureAD,
                    ProviderId = meData["id"]?.ToString() ?? string.Empty,
                    ObjectId = meData["id"]?.ToString(),
                    Username = meData["userPrincipalName"]?.ToString() ?? string.Empty,
                    Email = meData["mail"]?.ToString() ?? meData["userPrincipalName"]?.ToString() ?? string.Empty,
                    DisplayName = meData["displayName"]?.ToString() ?? string.Empty,
                    LastLoginAt = DateTime.Now
                };

                using var groupsRequest = new HttpRequestMessage(HttpMethod.Get, $"{GraphEndpoint}/me/memberOf");
                groupsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var groupsResponse = await _httpClient.SendAsync(groupsRequest).ConfigureAwait(false);

                if (groupsResponse.IsSuccessStatusCode)
                {
                    var groupsJson = await groupsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var groupsData = JObject.Parse(groupsJson);
                    if (groupsData["value"] is JArray groups)
                    {
                        foreach (var group in groups)
                        {
                            var groupName = group["displayName"]?.ToString();
                            var groupId = group["id"]?.ToString();
                            if (!string.IsNullOrEmpty(groupName))
                            {
                                user.Groups.Add(groupName);
                                user.Claims[$"group_{groupId}"] = groupName;
                            }
                        }
                    }
                }

                return user;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Azure AD GetUserInfoAsync failed");
                return null;
            }
        }

        public override async Task<string?> RefreshTokenAsync(string refreshToken)
        {
            // MSAL handles refresh tokens inside the user token cache; the
            // string supplied by the caller is intentionally ignored. This
            // method exists to satisfy the IAuthenticationProvider contract.
            try
            {
                if (!IsConfigured())
                {
                    return null;
                }

                var msalApp = await GetMsalAppAsync().ConfigureAwait(false);
                var accounts = await msalApp.GetAccountsAsync().ConfigureAwait(false);
                var account = accounts.FirstOrDefault();
                if (account == null)
                {
                    return null;
                }

                var tokenResult = await msalApp
                    .AcquireTokenSilent(_scopes, account)
                    .ExecuteAsync().ConfigureAwait(false);

                return tokenResult.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                // Refresh failed because user interaction is required.
                // Surface as null so the caller can re-prompt.
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Azure AD silent refresh failed");
                return null;
            }
        }

        public override async Task SignOutAsync(User user)
        {
            try
            {
                if (_msalApp == null)
                {
                    return;
                }

                var accounts = await _msalApp.GetAccountsAsync().ConfigureAwait(false);
                foreach (var account in accounts)
                {
                    await _msalApp.RemoveAsync(account).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "Azure AD sign-out for user {Username} did not remove the cached account",
                    user?.Username);
            }
        }

        private async Task<AppAuthenticationResult> BuildAuthenticationResultAsync(MsalAuthenticationResult tokenResult)
        {
            var user = await GetUserInfoAsync(tokenResult.AccessToken).ConfigureAwait(false);
            if (user == null)
            {
                return new AppAuthenticationResult
                {
                    Success = false,
                    ErrorMessage = LocalizationManager.S("auth.provider.user_info_unavailable")
                };
            }

            user.AccessToken = tokenResult.AccessToken;
            user.TokenExpiresAt = tokenResult.ExpiresOn.LocalDateTime;

            return new AppAuthenticationResult
            {
                Success = true,
                User = user,
                AccessToken = tokenResult.AccessToken,
                ExpiresAt = tokenResult.ExpiresOn.LocalDateTime
            };
        }

        private async Task<IPublicClientApplication> GetMsalAppAsync()
        {
            if (_msalApp != null)
            {
                return _msalApp;
            }

            await _initGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_msalApp != null)
                {
                    return _msalApp;
                }

                var clientId = GetConfigValue("ClientId");
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    throw new InvalidOperationException("Azure AD ClientId is not configured.");
                }

                var tenantId = GetConfigValue("TenantId");
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    tenantId = "organizations";
                }

                var authority = $"https://login.microsoftonline.com/{tenantId}";
                var redirectUri = GetConfigValue("RedirectUri");
                if (string.IsNullOrWhiteSpace(redirectUri))
                {
                    // http://localhost lets MSAL pick a free port on the
                    // loopback interface; matches the redirect URIs the
                    // Azure AD app registration template uses.
                    redirectUri = "http://localhost";
                }

                var app = PublicClientApplicationBuilder
                    .Create(clientId)
                    .WithAuthority(authority)
                    .WithRedirectUri(redirectUri)
                    .Build();

                await AttachPersistentCacheAsync(app, clientId).ConfigureAwait(false);

                _msalApp = app;
                return _msalApp;
            }
            finally
            {
                _initGate.Release();
            }
        }

        private async Task AttachPersistentCacheAsync(IPublicClientApplication app, string clientId)
        {
            try
            {
                var cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ExportAzureWiki",
                    CacheDirectoryName);
                Directory.CreateDirectory(cacheDir);

                // On Windows this uses DPAPI under the hood; CurrentUser
                // scope keeps the cache scoped to the signed-in Windows
                // user so a different user on the same box cannot read
                // the cache file.
                var storageProperties = new StorageCreationPropertiesBuilder(CacheFileName, cacheDir)
                    .WithCacheChangedEvent(clientId)
                    .Build();

                _cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
                _cacheHelper.RegisterCache(app.UserTokenCache);
            }
            catch (Exception ex)
            {
                // Persistent cache failure is non-fatal: the in-memory
                // cache still works for the current process. Logging it
                // means an operator can spot a permissions / disk issue
                // that is silently forcing re-prompts every restart.
                Log.Warning(ex,
                    "Azure AD persistent token cache could not be attached; falling back to in-memory only");
            }
        }

        private static string[] ParseScopes(string? configuredScopes)
        {
            if (string.IsNullOrWhiteSpace(configuredScopes))
            {
                return DefaultScopes;
            }

            var split = configuredScopes
                .Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return split.Length == 0 ? DefaultScopes : split;
        }
    }
}
