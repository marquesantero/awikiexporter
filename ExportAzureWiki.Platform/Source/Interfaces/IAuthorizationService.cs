using ExportAzureWiki.Models.Authentication;

namespace ExportAzureWiki.Interfaces
{
    public interface IAuthorizationService
    {
        Task<bool> HasPermissionAsync(string userId, string wikiId, PermissionLevel requiredLevel);
        Task<bool> HasPermissionAsync(User user, string wikiId, PermissionLevel requiredLevel);
        Task<List<string>> GetAccessibleWikisAsync(string userId);
        Task<PermissionLevel> GetEffectivePermissionAsync(string userId, string wikiId);
        List<AccessPolicy> GetAccessPolicies();
        AccessPolicy GetOrCreateAccessPolicy(AccessPolicyIdentityType identityType, string identityId, string identityDisplayName);
        void SaveAccessPolicy(AccessPolicy policy);
        Task<EffectiveSystemAccess> GetEffectiveSystemAccessAsync(string userId);
        Task<EffectiveWikiAccess> GetEffectiveWikiAccessAsync(string userId, string wikiId);
    }
}
