using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models.Authentication;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace ExportAzureWiki.Services.Authentication.Providers
{
    public class MicrosoftAccountProvider : BaseAuthenticationProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string AuthorizeEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
        private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
        private const string GraphEndpoint = "https://graph.microsoft.com/v1.0";

        public override AuthenticationProvider ProviderType => AuthenticationProvider.MicrosoftAccount;
        public override string ProviderName => "Microsoft Account";

        public MicrosoftAccountProvider(Dictionary<string, string> config) : base(config)
        {
        }

        public override bool IsConfigured()
        {
            return !string.IsNullOrEmpty(GetConfigValue("ClientId"));
        }

        public override async Task<AuthenticationResult> AuthenticateAsync(Dictionary<string, string>? parameters = null)
        {
            try
            {
                var clientId = GetConfigValue("ClientId");
                var redirectUri = GetConfigValue("RedirectUri") ?? "http://localhost:8080/callback";

                if (string.IsNullOrEmpty(clientId))
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.provider.microsoft.not_configured")
                    };
                }

                var scopes = "openid profile email User.Read";
                var state = Guid.NewGuid().ToString();
                var codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(codeVerifier);

                var authUrl = AuthorizeEndpoint +
                    $"?client_id={clientId}" +
                    $"&response_type=code" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&response_mode=query" +
                    $"&scope={Uri.EscapeDataString(scopes)}" +
                    $"&state={state}" +
                    $"&code_challenge={codeChallenge}" +
                    $"&code_challenge_method=S256";

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

                var tokenResult = await ExchangeCodeForTokenAsync(authCode, codeVerifier, redirectUri).ConfigureAwait(false);
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
                user.RefreshToken = tokenResult.RefreshToken;
                user.TokenExpiresAt = tokenResult.ExpiresAt;

                return new AuthenticationResult
                {
                    Success = true,
                    User = user,
                    AccessToken = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken,
                    ExpiresAt = tokenResult.ExpiresAt
                };
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = LocalizationManager.Sf("auth.provider.microsoft.auth_failed", ex.Message)
                };
            }
        }

        public override async Task<bool> ValidateTokenAsync(string token)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{GraphEndpoint}/me");
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
                var request = new HttpRequestMessage(HttpMethod.Get, $"{GraphEndpoint}/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = JObject.Parse(json);

                return new User
                {
                    Provider = AuthenticationProvider.MicrosoftAccount,
                    ProviderId = data["id"]?.ToString() ?? "",
                    Username = data["userPrincipalName"]?.ToString() ?? "",
                    Email = data["mail"]?.ToString() ?? data["userPrincipalName"]?.ToString() ?? "",
                    DisplayName = data["displayName"]?.ToString() ?? "",
                    LastLoginAt = DateTime.Now
                };
            }
            catch
            {
                return null;
            }
        }

        public override async Task<string?> RefreshTokenAsync(string refreshToken)
        {
            try
            {
                var clientId = GetConfigValue("ClientId");
                if (string.IsNullOrEmpty(clientId))
                    return null;

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scope"] = "openid profile email User.Read",
                    ["refresh_token"] = refreshToken,
                    ["grant_type"] = "refresh_token"
                });

                var response = await _httpClient.PostAsync(TokenEndpoint, content).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = JObject.Parse(json);

                return data["access_token"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public override Task SignOutAsync(User user)
        {
            return Task.CompletedTask;
        }

        private async Task<AuthenticationResult> ExchangeCodeForTokenAsync(string code, string codeVerifier, string redirectUri)
        {
            try
            {
                var clientId = GetConfigValue("ClientId");

                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId!,
                    ["scope"] = "openid profile email User.Read",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code",
                    ["code_verifier"] = codeVerifier
                });

                var response = await _httpClient.PostAsync(TokenEndpoint, content).ConfigureAwait(false);
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
                var expiresIn = int.Parse(data["expires_in"]?.ToString() ?? "3600");

                return new AuthenticationResult
                {
                    Success = true,
                    AccessToken = data["access_token"]?.ToString(),
                    RefreshToken = data["refresh_token"]?.ToString(),
                    ExpiresAt = DateTime.Now.AddSeconds(expiresIn)
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

        private string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(codeVerifier);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }
        }
    }
}
