using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Platform.Backend;

internal interface IAdminBackend
{
    Task<IReadOnlyList<UserRecord>> LoadUsersAsync();
    Task<IReadOnlyList<IdentityGroup>> LoadGroupsAsync();
    Task<IReadOnlyList<OAuthProvider>> LoadOAuthProvidersAsync();
    Task<IReadOnlyList<AiProvider>> LoadAiProvidersAsync();
    Task<AuthenticationConfiguration?> LoadAuthConfigurationAsync();
    Task<IReadOnlyList<AccessPolicy>> LoadAccessPoliciesAsync();
    Task<int> SaveUserAsync(UserRecord user, string? plainPassword = null);
    Task<bool> DeleteUserAsync(int id);
    Task<int> SaveGroupAsync(IdentityGroup group);
    Task<bool> DeleteGroupAsync(int id);
    Task<IDictionary<int, int>> LoadGroupMemberCountsAsync();
    Task<IReadOnlyList<WikiConfiguration>> LoadWikisAsync();
    Task<AccessPolicy> GetOrCreateAccessPolicyAsync(AccessPolicyIdentityType identityType, string identityId, string identityDisplayName);
    Task SaveAccessPolicyAsync(AccessPolicy policy);
    Task<int> SaveOAuthProviderAsync(OAuthProvider provider);
    Task<bool> DeleteOAuthProviderAsync(int id);
    Task<int> SaveAiProviderAsync(AiProvider provider);
    Task<bool> DeleteAiProviderAsync(int id);
    Task<bool> SaveAuthenticationConfigurationAsync(AuthenticationConfiguration configuration);
    Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm);
    Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm, int? providerId);
}





