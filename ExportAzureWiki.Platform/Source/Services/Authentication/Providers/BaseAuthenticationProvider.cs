using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Models.Authentication;

namespace ExportAzureWiki.Services.Authentication.Providers
{
    public abstract class BaseAuthenticationProvider : IAuthenticationProvider
    {
        protected readonly Dictionary<string, string> _config;

        public abstract AuthenticationProvider ProviderType { get; }
        public abstract string ProviderName { get; }

        protected BaseAuthenticationProvider(Dictionary<string, string> config)
        {
            _config = config ?? new Dictionary<string, string>();
        }

        public abstract Task<AuthenticationResult> AuthenticateAsync(Dictionary<string, string>? parameters = null);
        public abstract Task<bool> ValidateTokenAsync(string token);
        public abstract Task<User?> GetUserInfoAsync(string token);
        public abstract Task<string?> RefreshTokenAsync(string refreshToken);
        public abstract Task SignOutAsync(User user);
        public abstract bool IsConfigured();

        protected string? GetConfigValue(string key)
        {
            return _config.ContainsKey(key) ? _config[key] : null;
        }

        protected void SetConfigValue(string key, string value)
        {
            _config[key] = value;
        }
    }
}
