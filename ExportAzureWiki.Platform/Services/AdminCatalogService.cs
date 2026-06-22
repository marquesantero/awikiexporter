using ExportAzureWiki.Platform.Backend;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Platform.Services;

public sealed class AdminCatalogService : IAdminCatalogService
{
    private readonly IAdminBackend _backend;

    public AdminCatalogService()
        : this(new AdminBackend())
    {
    }

    internal AdminCatalogService(IAdminBackend backend)
    {
        _backend = backend;
    }

    public Task<IReadOnlyList<UserRecord>> LoadUsersAsync() => _backend.LoadUsersAsync();
    public Task<IReadOnlyList<IdentityGroup>> LoadGroupsAsync() => _backend.LoadGroupsAsync();
    public Task<IReadOnlyList<OAuthProvider>> LoadOAuthProvidersAsync() => _backend.LoadOAuthProvidersAsync();
    public Task<IReadOnlyList<AiProvider>> LoadAiProvidersAsync() => _backend.LoadAiProvidersAsync();
    public Task<AuthenticationConfiguration?> LoadAuthConfigurationAsync() => _backend.LoadAuthConfigurationAsync();
    public Task<IReadOnlyList<AccessPolicy>> LoadAccessPoliciesAsync() => _backend.LoadAccessPoliciesAsync();
    public Task<int> SaveUserAsync(UserRecord user, string? plainPassword = null) => _backend.SaveUserAsync(user, plainPassword);
    public Task<bool> DeleteUserAsync(int id) => _backend.DeleteUserAsync(id);
    public Task<int> SaveGroupAsync(IdentityGroup group) => _backend.SaveGroupAsync(group);
    public Task<bool> DeleteGroupAsync(int id) => _backend.DeleteGroupAsync(id);
    public Task<IDictionary<int, int>> LoadGroupMemberCountsAsync() => _backend.LoadGroupMemberCountsAsync();
    public Task<IReadOnlyList<WikiConfiguration>> LoadWikisAsync() => _backend.LoadWikisAsync();
    public Task<AccessPolicy> GetOrCreateAccessPolicyAsync(AccessPolicyIdentityType identityType, string identityId, string identityDisplayName)
        => _backend.GetOrCreateAccessPolicyAsync(identityType, identityId, identityDisplayName);
    public Task SaveAccessPolicyAsync(AccessPolicy policy) => _backend.SaveAccessPolicyAsync(policy);
    public Task<int> SaveOAuthProviderAsync(OAuthProvider provider) => _backend.SaveOAuthProviderAsync(provider);
    public Task<bool> DeleteOAuthProviderAsync(int id) => _backend.DeleteOAuthProviderAsync(id);
    public Task<int> SaveAiProviderAsync(AiProvider provider) => _backend.SaveAiProviderAsync(provider);
    public Task<bool> DeleteAiProviderAsync(int id) => _backend.DeleteAiProviderAsync(id);
    public Task<bool> SaveAuthenticationConfigurationAsync(AuthenticationConfiguration configuration)
        => _backend.SaveAuthenticationConfigurationAsync(configuration);
    public Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm)
        => _backend.SearchExternalUsersAsync(authMethod, searchTerm);
    public Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm, int? providerId)
        => _backend.SearchExternalUsersAsync(authMethod, searchTerm, providerId);
}




