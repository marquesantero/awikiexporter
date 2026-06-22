using System.Data;
using Dapper;
using ExportAzureWiki.Data;
using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Models.Authentication;

namespace ExportAzureWiki.Services.Authorization
{
    public class AuthorizationService : IAuthorizationService
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly List<AccessPolicy> _accessPolicies;

        public AuthorizationService()
        {
            _dbConnectionFactory = new DbConnectionFactory();
            _accessPolicies = LoadAccessPolicies();
        }

        public async Task<bool> HasPermissionAsync(string userId, string wikiId, PermissionLevel requiredLevel)
        {
            var effectiveLevel = await GetEffectivePermissionAsync(userId, wikiId).ConfigureAwait(false);
            return effectiveLevel >= requiredLevel;
        }

        public Task<bool> HasPermissionAsync(User user, string wikiId, PermissionLevel requiredLevel)
        {
            return HasPermissionAsync(user.Id, wikiId, requiredLevel);
        }

        public async Task<PermissionLevel> GetEffectivePermissionAsync(string userId, string wikiId)
        {
            var access = await GetEffectiveWikiAccessAsync(userId, wikiId).ConfigureAwait(false);
            return AccessPolicyEvaluator.ToPermissionLevel(access);
        }

        public async Task<List<string>> GetAccessibleWikisAsync(string userId)
        {
            var effectiveWikiAccess = await GetEffectiveWikiAccessMapAsync(userId).ConfigureAwait(false);
            return effectiveWikiAccess
                .Where(p => p.Value.CanView)
                .Select(p => p.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<AccessPolicy> GetAccessPolicies()
        {
            return _accessPolicies
                .Where(p => p.IsActive)
                .OrderBy(p => p.IdentityType)
                .ThenBy(p => p.IdentityDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public AccessPolicy GetOrCreateAccessPolicy(AccessPolicyIdentityType identityType, string identityId, string identityDisplayName)
        {
            var existing = _accessPolicies.FirstOrDefault(p =>
                p.IsActive &&
                p.IdentityType == identityType &&
                string.Equals(p.IdentityId, identityId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(identityDisplayName) &&
                    !string.Equals(existing.IdentityDisplayName, identityDisplayName, StringComparison.Ordinal))
                {
                    existing.IdentityDisplayName = identityDisplayName;
                    existing.LastModifiedAt = DateTime.Now;
                    SaveAccessPolicies();
                }

                return existing;
            }

            var created = new AccessPolicy
            {
                IdentityType = identityType,
                IdentityId = identityId,
                IdentityDisplayName = identityDisplayName,
                IsActive = true,
                CreatedAt = DateTime.Now,
                LastModifiedAt = DateTime.Now
            };

            _accessPolicies.Add(created);
            SaveAccessPolicies();
            return created;
        }

        public void SaveAccessPolicy(AccessPolicy policy)
        {
            var existing = _accessPolicies.FirstOrDefault(p => p.Id == policy.Id);
            policy.LastModifiedAt = DateTime.Now;

            if (existing == null)
            {
                _accessPolicies.Add(policy);
            }
            else
            {
                existing.IdentityType = policy.IdentityType;
                existing.IdentityId = policy.IdentityId;
                existing.IdentityDisplayName = policy.IdentityDisplayName;
                existing.IsAdmin = policy.IsAdmin;
                existing.System = policy.System ?? new SystemAccessPermissions();
                existing.Wikis = policy.Wikis ?? [];
                existing.IsActive = policy.IsActive;
                existing.LastModifiedAt = policy.LastModifiedAt;
            }

            SaveAccessPolicies();
        }

        public async Task<EffectiveSystemAccess> GetEffectiveSystemAccessAsync(string userId)
        {
            var applicable = await GetApplicablePoliciesAsync(userId).ConfigureAwait(false);
            return AccessPolicyEvaluator.EvaluateSystemAccess(applicable);
        }

        public async Task<EffectiveWikiAccess> GetEffectiveWikiAccessAsync(string userId, string wikiId)
        {
            var applicable = await GetApplicablePoliciesAsync(userId).ConfigureAwait(false);
            return AccessPolicyEvaluator.EvaluateWikiAccess(applicable, wikiId);
        }

        private List<AccessPolicy> LoadAccessPolicies()
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
                var databaseType = _dbConnectionFactory.GetDatabaseType();
                var policiesTable = GetAccessPoliciesTableName(databaseType);
                var wikiRulesTable = GetAccessPolicyWikisTableName(databaseType);

                var policyRows = connection.Query<AccessPolicyDbRow>(
                    $"""
                     SELECT
                        id AS Id,
                        identity_type AS IdentityType,
                        identity_id AS IdentityId,
                        identity_display_name AS IdentityDisplayName,
                        is_admin AS IsAdmin,
                        system_manage_wikis AS SystemManageWikis,
                        system_manage_users_and_groups AS SystemManageUsersAndGroups,
                        system_manage_permissions AS SystemManagePermissions,
                        created_at AS CreatedAt,
                        last_modified_at AS LastModifiedAt,
                        is_active AS IsActive
                     FROM {policiesTable}
                     """).ToList();

                var wikiRows = connection.Query<AccessPolicyWikiDbRow>(
                    $"""
                     SELECT
                        policy_id AS PolicyId,
                        wiki_id AS WikiId,
                        start_points AS StartPoints,
                        can_view AS CanView,
                        can_comment AS CanComment,
                        can_export_word AS CanExportWord,
                        can_export_pdf AS CanExportPdf,
                        can_use_letterhead AS CanUseLetterhead
                     FROM {wikiRulesTable}
                     """).ToList();

                return policyRows.Select(p => new AccessPolicy
                    {
                        Id = p.Id,
                        IdentityType = (AccessPolicyIdentityType)p.IdentityType,
                        IdentityId = p.IdentityId,
                        IdentityDisplayName = p.IdentityDisplayName,
                        IsAdmin = p.IsAdmin,
                        System = new SystemAccessPermissions
                        {
                            ManageWikis = p.SystemManageWikis,
                            ManageUsersAndGroups = p.SystemManageUsersAndGroups,
                            ManagePermissions = p.SystemManagePermissions
                        },
                        Wikis = wikiRows
                            .Where(w => string.Equals(w.PolicyId, p.Id, StringComparison.OrdinalIgnoreCase))
                            .Select(w => new WikiAccessRule
                            {
                                WikiId = w.WikiId,
                                StartPoints = w.StartPoints ?? string.Empty,
                                CanView = w.CanView,
                                CanComment = w.CanComment,
                                CanExportWord = w.CanExportWord,
                                CanExportPdf = w.CanExportPdf,
                                CanUseLetterhead = w.CanUseLetterhead
                            }).ToList(),
                        CreatedAt = p.CreatedAt,
                        LastModifiedAt = p.LastModifiedAt,
                        IsActive = p.IsActive
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                // Loading the policy set failed: every user falls back to no
                // permissions (fail closed). Log so the cause is visible
                // instead of presenting as a silent app-wide lockout.
                Serilog.Log.Error(ex, "Failed to load access policies; all users will be denied until this is resolved");
                return [];
            }
        }

        private void SaveAccessPolicies()
        {
            using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
            var databaseType = _dbConnectionFactory.GetDatabaseType();
            var policiesTable = GetAccessPoliciesTableName(databaseType);
            var wikiRulesTable = GetAccessPolicyWikisTableName(databaseType);

            using var transaction = connection.BeginTransaction();
            try
            {
                connection.Execute($"DELETE FROM {wikiRulesTable}", transaction: transaction);
                connection.Execute($"DELETE FROM {policiesTable}", transaction: transaction);

                foreach (var policy in _accessPolicies)
                {
                    connection.Execute(
                        $"""
                         INSERT INTO {policiesTable}
                         (
                             id, identity_type, identity_id, identity_display_name, is_admin,
                             system_manage_wikis, system_manage_users_and_groups, system_manage_permissions,
                             created_at, last_modified_at, is_active
                         )
                         VALUES
                         (
                             @Id, @IdentityType, @IdentityId, @IdentityDisplayName, @IsAdmin,
                             @SystemManageWikis, @SystemManageUsersAndGroups, @SystemManagePermissions,
                             @CreatedAt, @LastModifiedAt, @IsActive
                         )
                         """,
                        new
                        {
                            policy.Id,
                            IdentityType = (int)policy.IdentityType,
                            policy.IdentityId,
                            policy.IdentityDisplayName,
                            policy.IsAdmin,
                            SystemManageWikis = policy.System?.ManageWikis ?? false,
                            SystemManageUsersAndGroups = policy.System?.ManageUsersAndGroups ?? false,
                            SystemManagePermissions = policy.System?.ManagePermissions ?? false,
                            policy.CreatedAt,
                            policy.LastModifiedAt,
                            policy.IsActive
                        },
                        transaction: transaction);

                    foreach (var rule in policy.Wikis ?? [])
                    {
                        connection.Execute(
                            $"""
                             INSERT INTO {wikiRulesTable}
                             (
                                 policy_id, wiki_id, start_points,
                                 can_view, can_comment, can_export_word, can_export_pdf, can_use_letterhead
                             )
                             VALUES
                             (
                                 @PolicyId, @WikiId, @StartPoints,
                                 @CanView, @CanComment, @CanExportWord, @CanExportPdf, @CanUseLetterhead
                             )
                             """,
                            new
                            {
                                PolicyId = policy.Id,
                                WikiId = rule.WikiId,
                                StartPoints = rule.StartPoints ?? string.Empty,
                                rule.CanView,
                                rule.CanComment,
                                rule.CanExportWord,
                                rule.CanExportPdf,
                                rule.CanUseLetterhead
                            },
                            transaction: transaction);
                    }
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static string GetAccessPoliciesTableName(DatabaseType databaseType)
        {
            return databaseType == DatabaseType.SqlServer ? "[dbo].[AccessPolicies]" : "access_policies";
        }

        private static string GetAccessPolicyWikisTableName(DatabaseType databaseType)
        {
            return databaseType == DatabaseType.SqlServer ? "[dbo].[AccessPolicyWikis]" : "access_policy_wikis";
        }

        private async Task<Dictionary<string, EffectiveWikiAccess>> GetEffectiveWikiAccessMapAsync(string userId)
        {
            var applicable = await GetApplicablePoliciesAsync(userId).ConfigureAwait(false);
            return AccessPolicyEvaluator.BuildWikiAccessMap(applicable);
        }

        private async Task<List<AccessPolicy>> GetApplicablePoliciesAsync(string userId)
        {
            var groupIds = await GetUserGroupIdsAsync(userId).ConfigureAwait(false);
            return AccessPolicyEvaluator.FilterApplicable(_accessPolicies, userId, groupIds);
        }

        private async Task<HashSet<string>> GetUserGroupIdsAsync(string userId)
        {
            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!int.TryParse(userId, out var numericUserId))
            {
                return groupIds;
            }

            try
            {
                using var uow = new UnitOfWork(_dbConnectionFactory);
                var groups = await uow.Groups.GetByUserIdAsync(numericUserId).ConfigureAwait(false);
                foreach (var group in groups)
                {
                    groupIds.Add(group.Id.ToString());
                }
            }
            catch (Exception ex)
            {
                // Group lookup failure degrades the user to direct policies
                // only. That fails closed for group-granted permissions, but
                // logging it means an operator can tell a permission complaint
                // apart from a DB outage. See Fase 1.4 / 4.4.
                Serilog.Log.Warning(ex,
                    "Group lookup failed for user {UserId}; evaluating direct policies only",
                    userId);
            }

            return groupIds;
        }

        private sealed class AccessPolicyDbRow
        {
            public string Id { get; set; } = string.Empty;
            public int IdentityType { get; set; }
            public string IdentityId { get; set; } = string.Empty;
            public string IdentityDisplayName { get; set; } = string.Empty;
            public bool IsAdmin { get; set; }
            public bool SystemManageWikis { get; set; }
            public bool SystemManageUsersAndGroups { get; set; }
            public bool SystemManagePermissions { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastModifiedAt { get; set; }
            public bool IsActive { get; set; }
        }

        private sealed class AccessPolicyWikiDbRow
        {
            public string PolicyId { get; set; } = string.Empty;
            public string WikiId { get; set; } = string.Empty;
            public string? StartPoints { get; set; }
            public bool CanView { get; set; }
            public bool CanComment { get; set; }
            public bool CanExportWord { get; set; }
            public bool CanExportPdf { get; set; }
            public bool CanUseLetterhead { get; set; }
        }
    }
}
