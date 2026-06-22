using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Data;
using EntityOAuthProvider = ExportAzureWiki.Models.OAuthProvider;
using ExportAzureWiki.Services;
using ExportAzureWiki.Services.Authorization;
using System.DirectoryServices.AccountManagement;
using System.Net.Http.Headers;
using System.Text.Json;
using CoreAccessPolicy = ExportAzureWiki.Core.Models.AccessPolicy;
using CoreAccessPolicyIdentityType = ExportAzureWiki.Core.Models.AccessPolicyIdentityType;

namespace ExportAzureWiki.Platform.Backend;

internal sealed class AdminBackend : IAdminBackend
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string GitHubApiBaseUrl = "https://api.github.com";
    private static readonly HttpClient HttpClient = new();

    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly PasswordHashingService _passwordHashingService;
    private readonly OAuthProviderFactoryService _oauthProviderFactoryService;
    private readonly Dictionary<string, (string Token, DateTime ExpiresAtUtc)> _tokenCache = new(StringComparer.OrdinalIgnoreCase);

    public AdminBackend()
        : this(new DbConnectionFactory(), new PasswordHashingService(), new OAuthProviderFactoryService())
    {
    }

    internal AdminBackend(IDbConnectionFactory dbConnectionFactory, PasswordHashingService passwordHashingService, OAuthProviderFactoryService oauthProviderFactoryService)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _passwordHashingService = passwordHashingService;
        _oauthProviderFactoryService = oauthProviderFactoryService;
    }

    public async Task<IReadOnlyList<UserRecord>> LoadUsersAsync()
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var users = await uow.Users.GetAllAsync().ConfigureAwait(false);
        return users
            .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(ProviderModelMapper.ToCore)
            .ToList();
    }

    public async Task<IReadOnlyList<IdentityGroup>> LoadGroupsAsync()
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var groups = await uow.Groups.GetAllAsync().ConfigureAwait(false);
        return groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ProviderModelMapper.ToCore)
            .ToList();
    }

    public async Task<IReadOnlyList<OAuthProvider>> LoadOAuthProvidersAsync()
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var providers = await uow.OAuthProviders.GetAllAsync().ConfigureAwait(false);
        return providers
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ProviderModelMapper.ToCore)
            .ToList();
    }

    public async Task<IReadOnlyList<AiProvider>> LoadAiProvidersAsync()
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var providers = await uow.AiProviders.GetAllAsync().ConfigureAwait(false);
        return providers
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ProviderModelMapper.ToCore)
            .ToList();
    }

    public async Task<AuthenticationConfiguration?> LoadAuthConfigurationAsync()
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var value = await uow.AuthenticationConfiguration.GetConfigurationAsync().ConfigureAwait(false);
        return value == null ? null : ProviderModelMapper.ToCore(value);
    }

    public Task<IReadOnlyList<CoreAccessPolicy>> LoadAccessPoliciesAsync()
    {
        var service = new AuthorizationService();
        IReadOnlyList<CoreAccessPolicy> policies = service.GetAccessPolicies()
            .Where(p => p.IsActive)
            .OrderBy(p => p.IdentityDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ProviderModelMapper.ToCore)
            .ToList();
        return Task.FromResult(policies);
    }

    public async Task<int> SaveUserAsync(UserRecord user, string? plainPassword = null)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var entity = ProviderModelMapper.ToProvider(user);

        if (!string.IsNullOrWhiteSpace(plainPassword))
        {
            var passwordData = _passwordHashingService.HashPassword(plainPassword);
            entity.PasswordHash = passwordData.hash;
            entity.PasswordSalt = passwordData.salt;
        }

        if (entity.Id <= 0)
        {
            entity.CreatedAt = DateTime.Now;
            var id = await uow.Users.AddAsync(entity).ConfigureAwait(false);
            user.Id = id;
            return id;
        }

        entity.LastModifiedAt = DateTime.Now;
        await uow.Users.UpdateUserSafeAsync(entity).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        return await uow.Users.DeleteAsync(id).ConfigureAwait(false);
    }

    public async Task<int> SaveGroupAsync(IdentityGroup group)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var entity = ProviderModelMapper.ToProvider(group);

        if (entity.Id <= 0)
        {
            entity.CreatedAt = DateTime.Now;
            var id = await uow.Groups.AddAsync(entity).ConfigureAwait(false);
            group.Id = id;
            return id;
        }

        await uow.Groups.UpdateAsync(entity).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<bool> DeleteGroupAsync(int id)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        return await uow.Groups.DeleteAsync(id).ConfigureAwait(false);
    }

    public async Task<IDictionary<int, int>> LoadGroupMemberCountsAsync()
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var groups = await uow.Groups.GetAllAsync().ConfigureAwait(false);
        var result = new Dictionary<int, int>();
        foreach (var group in groups)
        {
            var members = await uow.Groups.GetUsersByGroupIdAsync(group.Id).ConfigureAwait(false);
            result[group.Id] = members.Count();
        }

        return result;
    }

    public Task<IReadOnlyList<WikiConfiguration>> LoadWikisAsync()
    {
        var storage = new WikiConfigurationStorageService(_dbConnectionFactory);
        var items = storage.LoadAll()
            .Select(ProviderModelMapper.ToCore)
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<WikiConfiguration>>(items);
    }

    public Task<CoreAccessPolicy> GetOrCreateAccessPolicyAsync(CoreAccessPolicyIdentityType identityType, string identityId, string identityDisplayName)
    {
        var service = new AuthorizationService();
        var policy = service.GetOrCreateAccessPolicy((ExportAzureWiki.Models.Authentication.AccessPolicyIdentityType)identityType, identityId, identityDisplayName);
        return Task.FromResult(ProviderModelMapper.ToCore(policy));
    }

    public Task SaveAccessPolicyAsync(CoreAccessPolicy policy)
    {
        var service = new AuthorizationService();
        service.SaveAccessPolicy(ProviderModelMapper.ToProvider(policy));
        return Task.CompletedTask;
    }

    public async Task<int> SaveOAuthProviderAsync(OAuthProvider provider)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);

        if (provider.Id <= 0)
        {
            provider.CreatedAt = DateTime.Now;
            provider.LastModifiedAt = DateTime.Now;
            var id = await uow.OAuthProviders.AddAsync(ProviderModelMapper.ToProvider(provider)).ConfigureAwait(false);
            provider.Id = id;
            return id;
        }

        provider.LastModifiedAt = DateTime.Now;
        await uow.OAuthProviders.UpdateSafeAsync(ProviderModelMapper.ToProvider(provider)).ConfigureAwait(false);
        return provider.Id;
    }

    public async Task<bool> DeleteOAuthProviderAsync(int id)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        return await uow.OAuthProviders.DeleteAsync(id).ConfigureAwait(false);
    }

    public async Task<int> SaveAiProviderAsync(AiProvider provider)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);

        if (provider.Id <= 0)
        {
            provider.CreatedAt = DateTime.Now;
            provider.LastModifiedAt = DateTime.Now;
            var id = await uow.AiProviders.AddAsync(ProviderModelMapper.ToProvider(provider)).ConfigureAwait(false);
            provider.Id = id;
            return id;
        }

        provider.LastModifiedAt = DateTime.Now;
        await uow.AiProviders.UpdateSafeAsync(ProviderModelMapper.ToProvider(provider)).ConfigureAwait(false);
        return provider.Id;
    }

    public async Task<bool> DeleteAiProviderAsync(int id)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        return await uow.AiProviders.DeleteAsync(id).ConfigureAwait(false);
    }

    public async Task<bool> SaveAuthenticationConfigurationAsync(AuthenticationConfiguration configuration)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        return await uow.AuthenticationConfiguration.SaveConfigurationAsync(ProviderModelMapper.ToProvider(configuration)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm)
        => await SearchExternalUsersAsync(authMethod, searchTerm, null).ConfigureAwait(false);

    public async Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm, int? providerId)
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var providers = (await uow.OAuthProviders.GetEnabledProvidersAsync().ConfigureAwait(false)).ToList();

        return authMethod switch
        {
            AuthenticationMethod.AzureAD => await SearchAzureUsersAsync(providers, searchTerm, providerId).ConfigureAwait(false),
            AuthenticationMethod.OAuth => await SearchGitHubUsersAsync(providers, searchTerm, providerId).ConfigureAwait(false),
            AuthenticationMethod.Windows => await SearchWindowsUsersAsync(searchTerm).ConfigureAwait(false),
            _ => []
        };
    }

    private static Task<IReadOnlyList<ExternalDirectoryUser>> SearchWindowsUsersAsync(string? searchTerm)
    {
        var term = (searchTerm ?? string.Empty).Trim();
        var users = new List<ExternalDirectoryUser>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var source = "Domain";
        var loadedFromDomain = TryAppendWindowsUsers(ContextType.Domain, term, users, unique, source);
        if (!loadedFromDomain || users.Count == 0)
        {
            source = "Machine";
            TryAppendWindowsUsers(ContextType.Machine, term, users, unique, source);
        }

        IReadOnlyList<ExternalDirectoryUser> result = users
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();

        return Task.FromResult(result);
    }

    private async Task<IReadOnlyList<ExternalDirectoryUser>> SearchAzureUsersAsync(
        IReadOnlyList<EntityOAuthProvider> providers,
        string? searchTerm,
        int? providerId)
    {
        var provider = providers
            .Where(p => string.Equals(p.ProviderName, "AzureAD", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.ProviderName, "Microsoft", StringComparison.OrdinalIgnoreCase))
            .Where(p => !providerId.HasValue || p.Id == providerId.Value)
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (provider == null)
        {
            return [];
        }

        var token = await EnsureAccessTokenAsync(provider).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        var url = $"{GraphBaseUrl}/users?$select=id,displayName,userPrincipalName,mail,accountEnabled&$top=100";
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var s = EscapeODataString(searchTerm.Trim());
            var filter = $"accountEnabled eq true and (startswith(displayName,'{s}') or startswith(userPrincipalName,'{s}') or startswith(mail,'{s}'))";
            url += $"&$filter={Uri.EscapeDataString(filter)}";
        }
        else
        {
            url += $"&$filter={Uri.EscapeDataString("accountEnabled eq true")}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);
        var values = doc.RootElement.TryGetProperty("value", out var valueElement)
            ? valueElement.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

        return values
            .Select(x => new ExternalDirectoryUser
            {
                ExternalId = GetJsonString(x, "id"),
                DisplayName = GetJsonString(x, "displayName"),
                Username = GetJsonString(x, "userPrincipalName"),
                Email = GetJsonString(x, "mail"),
                IsActive = GetJsonBool(x, "accountEnabled", true),
                ProviderName = string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.ProviderName : provider.DisplayName
            })
            .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.ExternalId) && !string.IsNullOrWhiteSpace(x.Username))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<ExternalDirectoryUser>> SearchGitHubUsersAsync(
        IReadOnlyList<EntityOAuthProvider> providers,
        string? searchTerm,
        int? providerId)
    {
        var provider = providers
            .Where(p => string.Equals(p.ProviderName, "GitHub", StringComparison.OrdinalIgnoreCase))
            .Where(p => !providerId.HasValue || p.Id == providerId.Value)
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (provider == null)
        {
            return [];
        }

        var token = await EnsureAccessTokenAsync(provider).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        var query = string.IsNullOrWhiteSpace(searchTerm) ? "type:user" : $"{searchTerm.Trim()} type:user";
        var url = $"{GitHubApiBaseUrl}/search/users?q={Uri.EscapeDataString(query)}&per_page=50";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("AWikiExporter/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);
        var values = doc.RootElement.TryGetProperty("items", out var valueElement)
            ? valueElement.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

        return values
            .Select(x =>
            {
                var login = GetJsonString(x, "login");
                return new ExternalDirectoryUser
                {
                    ExternalId = GetJsonString(x, "id"),
                    DisplayName = login,
                    Username = login,
                    Email = string.Empty,
                    IsActive = true,
                    ProviderName = string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.ProviderName : provider.DisplayName
                };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.ExternalId) && !string.IsNullOrWhiteSpace(x.Username))
            .OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string?> EnsureAccessTokenAsync(EntityOAuthProvider provider)
    {
        var cacheKey = provider.Id <= 0 ? provider.ProviderName : $"{provider.ProviderName}:{provider.Id}";
        if (_tokenCache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow < entry.ExpiresAtUtc.AddMinutes(-1))
        {
            return entry.Token;
        }

        var authProvider = _oauthProviderFactoryService.CreateProvider(provider);
        var result = await authProvider.AuthenticateAsync().ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.AccessToken))
        {
            return null;
        }

        var expiresAt = (result.ExpiresAt ?? DateTime.UtcNow.AddMinutes(20)).ToUniversalTime();
        _tokenCache[cacheKey] = (result.AccessToken, expiresAt);
        return result.AccessToken;
    }

    private static string EscapeODataString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static bool GetJsonBool(JsonElement element, string propertyName, bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind == JsonValueKind.True || (value.ValueKind != JsonValueKind.False && defaultValue);
    }

    private static bool TryAppendWindowsUsers(
        ContextType contextType,
        string term,
        List<ExternalDirectoryUser> target,
        HashSet<string> unique,
        string source)
    {
        try
        {
            using var context = new PrincipalContext(contextType);
            using var queryFilter = new UserPrincipal(context) { Enabled = true };
            using var searcher = new PrincipalSearcher(queryFilter);

            foreach (var principal in searcher.FindAll())
            {
                using var user = principal as UserPrincipal;
                if (user == null)
                {
                    principal.Dispose();
                    continue;
                }

                var username = (user.SamAccountName ?? user.UserPrincipalName ?? string.Empty).Trim();
                var display = (user.DisplayName ?? user.Name ?? username).Trim();
                var email = (user.EmailAddress ?? string.Empty).Trim();
                var externalId = user.Sid?.Value ?? username;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(externalId))
                {
                    principal.Dispose();
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(term) &&
                    !ContainsIgnoreCase(username, term) &&
                    !ContainsIgnoreCase(display, term) &&
                    !ContainsIgnoreCase(email, term))
                {
                    principal.Dispose();
                    continue;
                }

                if (!unique.Add(externalId))
                {
                    principal.Dispose();
                    continue;
                }

                target.Add(new ExternalDirectoryUser
                {
                    ExternalId = externalId,
                    Username = username,
                    DisplayName = string.IsNullOrWhiteSpace(display) ? username : display,
                    Email = email,
                    IsActive = true,
                    ProviderName = source
                });

                principal.Dispose();
                if (target.Count >= 200)
                {
                    break;
                }
            }

            return true;
        }
        catch
        {
            // Ignore source-specific errors and keep fallback behavior.
            return false;
        }
    }

    private static bool ContainsIgnoreCase(string source, string value)
        => source?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

}





