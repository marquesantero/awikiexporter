using ExportAzureWiki.Models.Authentication;

namespace ExportAzureWiki.Services.Authorization;

/// <summary>
/// Pure permission-resolution logic, extracted from
/// <see cref="AuthorizationService"/> so the security-critical matrix can
/// be unit-tested without a database.
///
/// The service still owns I/O: loading policies, resolving which groups a
/// user belongs to. Once it has the set of <em>applicable</em> policies
/// (direct user policies + the user's group policies) it hands them here
/// for the actual decision.
///
/// Resolution rules (unchanged from the original inline implementation):
/// <list type="bullet">
///   <item>Admin override: any applicable policy with <c>IsAdmin</c> grants
///   full system and full wiki access.</item>
///   <item>Wiki rules OR-combine across every applicable policy whose rule
///   matches the wiki id (case-insensitive). Start points are merged,
///   de-duplicated, and joined with '|'.</item>
///   <item>System flags OR-combine <c>IsAdmin</c> with the specific
///   <c>System.*</c> grant.</item>
///   <item>Effective level: None when the user cannot view; Admin when an
///   applicable policy is admin; Write when commenting is allowed;
///   otherwise Read.</item>
/// </list>
/// </summary>
public static class AccessPolicyEvaluator
{
    /// <summary>
    /// Filters the full policy set down to the ones that apply to the user:
    /// active direct-user policies plus active group policies for groups the
    /// user belongs to. Pure: the caller supplies the resolved group ids.
    /// </summary>
    public static List<AccessPolicy> FilterApplicable(
        IEnumerable<AccessPolicy> allPolicies,
        string userId,
        ISet<string> userGroupIds)
    {
        ArgumentNullException.ThrowIfNull(allPolicies);
        ArgumentNullException.ThrowIfNull(userGroupIds);

        var active = allPolicies.Where(p => p.IsActive).ToList();

        var direct = active.Where(p =>
            p.IdentityType == AccessPolicyIdentityType.User &&
            string.Equals(p.IdentityId, userId, StringComparison.OrdinalIgnoreCase));

        var groups = active.Where(p =>
            p.IdentityType == AccessPolicyIdentityType.Group &&
            userGroupIds.Contains(p.IdentityId));

        return direct.Concat(groups).ToList();
    }

    public static EffectiveSystemAccess EvaluateSystemAccess(IReadOnlyCollection<AccessPolicy> applicable)
    {
        ArgumentNullException.ThrowIfNull(applicable);

        if (applicable.Any(p => p.IsAdmin))
        {
            return new EffectiveSystemAccess
            {
                IsAdmin = true,
                CanManagePermissions = true,
                CanManageUsersAndGroups = true,
                CanManageWikis = true
            };
        }

        return new EffectiveSystemAccess
        {
            IsAdmin = false,
            CanManageWikis = applicable.Any(p => p.System?.ManageWikis == true),
            CanManageUsersAndGroups = applicable.Any(p => p.System?.ManageUsersAndGroups == true),
            CanManagePermissions = applicable.Any(p => p.System?.ManagePermissions == true)
        };
    }

    public static EffectiveWikiAccess EvaluateWikiAccess(IReadOnlyCollection<AccessPolicy> applicable, string wikiId)
    {
        ArgumentNullException.ThrowIfNull(applicable);

        if (applicable.Any(p => p.IsAdmin))
        {
            return new EffectiveWikiAccess
            {
                IsAdmin = true,
                CanView = true,
                CanComment = true,
                CanExportWord = true,
                CanExportPdf = true,
                CanUseLetterhead = true,
                StartPoints = string.Empty
            };
        }

        var rules = applicable
            .SelectMany(p => p.Wikis ?? [])
            .Where(w => string.Equals(w.WikiId, wikiId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rules.Count == 0)
        {
            return new EffectiveWikiAccess();
        }

        var combinedStartPoints = string.Join("|", rules
            .SelectMany(r => ParseStartPoints(r.StartPoints))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return new EffectiveWikiAccess
        {
            IsAdmin = false,
            CanView = rules.Any(r => r.CanView),
            CanComment = rules.Any(r => r.CanComment),
            CanExportWord = rules.Any(r => r.CanExportWord),
            CanExportPdf = rules.Any(r => r.CanExportPdf),
            CanUseLetterhead = rules.Any(r => r.CanUseLetterhead),
            StartPoints = combinedStartPoints
        };
    }

    /// <summary>
    /// Maps an effective wiki access into the coarse
    /// <see cref="PermissionLevel"/> ladder the rest of the app checks
    /// against.
    /// </summary>
    public static PermissionLevel ToPermissionLevel(EffectiveWikiAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (!access.CanView)
        {
            return PermissionLevel.None;
        }

        if (access.IsAdmin)
        {
            return PermissionLevel.Admin;
        }

        return access.CanComment ? PermissionLevel.Write : PermissionLevel.Read;
    }

    /// <summary>
    /// Builds the per-wiki access map used by "which wikis can this user
    /// see". Admins get an empty map by contract (the caller treats admin
    /// separately); non-admins get an OR-combined entry per wiki id.
    /// </summary>
    public static Dictionary<string, EffectiveWikiAccess> BuildWikiAccessMap(IReadOnlyCollection<AccessPolicy> applicable)
    {
        ArgumentNullException.ThrowIfNull(applicable);

        var result = new Dictionary<string, EffectiveWikiAccess>(StringComparer.OrdinalIgnoreCase);

        if (applicable.Any(p => p.IsAdmin))
        {
            return result;
        }

        foreach (var rule in applicable.SelectMany(p => p.Wikis ?? []))
        {
            if (string.IsNullOrWhiteSpace(rule.WikiId))
            {
                continue;
            }

            if (!result.TryGetValue(rule.WikiId, out var access))
            {
                access = new EffectiveWikiAccess();
                result[rule.WikiId] = access;
            }

            access.CanView |= rule.CanView;
            access.CanComment |= rule.CanComment;
            access.CanExportWord |= rule.CanExportWord;
            access.CanExportPdf |= rule.CanExportPdf;
            access.CanUseLetterhead |= rule.CanUseLetterhead;
            access.StartPoints = string.Join("|", ParseStartPoints(access.StartPoints)
                .Concat(ParseStartPoints(rule.StartPoints))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        return result;
    }

    public static HashSet<string> ParseStartPoints(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
