using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models.Authentication;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace ExportAzureWiki.Services.Authentication.Providers
{
    public class GitHubProvider : BaseAuthenticationProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string AuthorizeEndpoint = "https://github.com/login/oauth/authorize";
        private const string TokenEndpoint = "https://github.com/login/oauth/access_token";
        private const string ApiEndpoint = "https://api.github.com";

        public override AuthenticationProvider ProviderType => AuthenticationProvider.GitHub;
        public override string ProviderName => "GitHub";

        public GitHubProvider(Dictionary<string, string> config) : base(config)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ExportAzureWiki/1.0");
        }

        public override bool IsConfigured()
        {
            return !string.IsNullOrEmpty(GetConfigValue("ClientId")) &&
                   !string.IsNullOrEmpty(GetConfigValue("ClientSecret"));
        }

        public override async Task<AuthenticationResult> AuthenticateAsync(Dictionary<string, string>? parameters = null)
        {
            try
            {
                var clientId = GetConfigValue("ClientId");
                var clientSecret = GetConfigValue("ClientSecret");
                var redirectUri = GetConfigValue("RedirectUri") ?? "http://localhost:8080/callback";

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.provider.github.not_configured")
                    };
                }

                var scopes = "read:user user:email read:org";
                var state = Guid.NewGuid().ToString();

                var authUrl = $"{AuthorizeEndpoint}" +
                    $"?client_id={clientId}" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&scope={Uri.EscapeDataString(scopes)}" +
                    $"&state={state}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });

                var authCode = await ListenForCallbackAsync(redirectUri, state).ConfigureAwait(false);
                if (string.IsNullOrEmpty(authCode))
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.provider.authentication_cancelled_or_failed")
                    };
                }

                var tokenResult = await ExchangeCodeForTokenAsync(authCode, redirectUri).ConfigureAwait(false);
                if (!tokenResult.Success)
                {
                    return tokenResult;
                }

                var user = await GetUserInfoAsync(tokenResult.AccessToken!).ConfigureAwait(false);
                if (user == null)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.provider.user_info_unavailable")
                    };
                }

                user.AccessToken = tokenResult.AccessToken;
                user.TokenExpiresAt = DateTime.Now.AddDays(365);

                return new AuthenticationResult
                {
                    Success = true,
                    User = user,
                    AccessToken = tokenResult.AccessToken,
                    ExpiresAt = user.TokenExpiresAt
                };
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = LocalizationManager.Sf("auth.provider.github.auth_failed", ex.Message)
                };
            }
        }

        public override async Task<bool> ValidateTokenAsync(string token)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiEndpoint}/user");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public override async Task<User?> GetUserInfoAsync(string token)
        {
            try
            {
                var userRequest = new HttpRequestMessage(HttpMethod.Get, $"{ApiEndpoint}/user");
                userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var userResponse = await _httpClient.SendAsync(userRequest).ConfigureAwait(false);

                if (!userResponse.IsSuccessStatusCode)
                    return null;

                var userJson = await userResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var userData = JObject.Parse(userJson);

                var user = new User
                {
                    Provider = AuthenticationProvider.GitHub,
                    ProviderId = userData["id"]?.ToString() ?? "",
                    Username = userData["login"]?.ToString() ?? "",
                    Email = userData["email"]?.ToString() ?? "",
                    DisplayName = userData["name"]?.ToString() ?? userData["login"]?.ToString() ?? "",
                    AvatarUrl = userData["avatar_url"]?.ToString(),
                    GitHubLogin = userData["login"]?.ToString(),
                    LastLoginAt = DateTime.Now
                };

                var orgsRequest = new HttpRequestMessage(HttpMethod.Get, $"{ApiEndpoint}/user/orgs");
                orgsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var orgsResponse = await _httpClient.SendAsync(orgsRequest).ConfigureAwait(false);

                if (orgsResponse.IsSuccessStatusCode)
                {
                    var orgsJson = await orgsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var orgs = JArray.Parse(orgsJson);

                    foreach (var org in orgs)
                    {
                        var orgLogin = org["login"]?.ToString();
                        if (!string.IsNullOrEmpty(orgLogin))
                        {
                            user.GitHubOrganizations.Add(orgLogin);
                            user.Groups.Add($"GitHub:{orgLogin}");
                        }
                    }
                }

                return user;
            }
            catch
            {
                return null;
            }
        }

        public override Task<string?> RefreshTokenAsync(string refreshToken)
        {
            return Task.FromResult<string?>(null);
        }

        public override Task SignOutAsync(User user)
        {
            return Task.CompletedTask;
        }

        private async Task<AuthenticationResult> ExchangeCodeForTokenAsync(string code, string redirectUri)
        {
            try
            {
                var clientId = GetConfigValue("ClientId");
                var clientSecret = GetConfigValue("ClientSecret");

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId!,
                    ["client_secret"] = clientSecret!,
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri
                });

                var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
                {
                    Content = content
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.Sf("auth.provider.token_exchange_failed", json)
                    };
                }

                var data = JObject.Parse(json);
                var accessToken = data["access_token"]?.ToString();

                if (string.IsNullOrEmpty(accessToken))
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.provider.no_access_token")
                    };
                }

                return new AuthenticationResult
                {
                    Success = true,
                    AccessToken = accessToken,
                    ExpiresAt = DateTime.Now.AddDays(365)
                };
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = LocalizationManager.Sf("auth.provider.token_exchange_error", ex.Message)
                };
            }
        }

        private async Task<string?> ListenForCallbackAsync(string redirectUri, string expectedState)
        {
            var uri = new Uri(redirectUri);
            var listener = new System.Net.HttpListener();
            listener.Prefixes.Add($"http://localhost:{uri.Port}/");
            listener.Start();

            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                var query = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");

                var response = context.Response;
                var responseString = "<html><body><h1>Authentication successful!</h1><p>You can close this window.</p><script>window.close();</script></body></html>";
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                response.OutputStream.Close();

                var state = query["state"];
                if (state != expectedState)
                    return null;

                return query["code"];
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
