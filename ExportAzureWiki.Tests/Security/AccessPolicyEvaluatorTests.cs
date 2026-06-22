using ExportAzureWiki.Models.Authentication;
using ExportAzureWiki.Services.Authorization;

namespace ExportAzureWiki.Tests.Security;

/// <summary>
/// Exhaustive coverage of the permission-resolution matrix. This is the
/// security-critical decision logic: a regression here either locks out
/// legitimate users or, worse, grants access that should be denied.
/// </summary>
public sealed class AccessPolicyEvaluatorTests
{
    private const string Wiki = "wiki-1";
    private const string OtherWiki = "wiki-2";

    private static AccessPolicy UserPolicy(string userId, Action<AccessPolicy> configure)
    {
        var policy = new AccessPolicy
        {
            IdentityType = AccessPolicyIdentityType.User,
            IdentityId = userId,
            IsActive = true,
        };
        configure(policy);
        return policy;
    }

    private static AccessPolicy GroupPolicy(string groupId, Action<AccessPolicy> configure)
    {
        var policy = new AccessPolicy
        {
            IdentityType = AccessPolicyIdentityType.Group,
            IdentityId = groupId,
            IsActive = true,
        };
        configure(policy);
        return policy;
    }

    private static WikiAccessRule Rule(string wikiId, Action<WikiAccessRule> configure)
    {
        var rule = new WikiAccessRule { WikiId = wikiId };
        configure(rule);
        return rule;
    }

    // ---- FilterApplicable -------------------------------------------------

    [Fact]
    public void FilterApplicable_Includes_Direct_User_Policy()
    {
        var policies = new[] { UserPolicy("42", p => p.IsAdmin = true) };
        var applicable = AccessPolicyEvaluator.FilterApplicable(policies, "42", new HashSet<string>());
        applicable.Should().ContainSingle();
    }

    [Fact]
    public void FilterApplicable_Excludes_Other_Users_Policy()
    {
        var policies = new[] { UserPolicy("99", p => p.IsAdmin = true) };
        var applicable = AccessPolicyEvaluator.FilterApplicable(policies, "42", new HashSet<string>());
        applicable.Should().BeEmpty();
    }

    [Fact]
    public void FilterApplicable_Includes_Group_Policy_For_Member()
    {
        var policies = new[] { GroupPolicy("7", p => p.IsAdmin = true) };
        var applicable = AccessPolicyEvaluator.FilterApplicable(policies, "42", new HashSet<string> { "7" });
        applicable.Should().ContainSingle();
    }

    [Fact]
    public void FilterApplicable_Excludes_Group_Policy_For_NonMember()
    {
        var policies = new[] { GroupPolicy("7", p => p.IsAdmin = true) };
        var applicable = AccessPolicyEvaluator.FilterApplicable(policies, "42", new HashSet<string> { "8" });
        applicable.Should().BeEmpty();
    }

    [Fact]
    public void FilterApplicable_Excludes_Inactive_Policies()
    {
        var policies = new[] { UserPolicy("42", p => { p.IsAdmin = true; p.IsActive = false; }) };
        var applicable = AccessPolicyEvaluator.FilterApplicable(policies, "42", new HashSet<string>());
        applicable.Should().BeEmpty();
    }

    [Fact]
    public void FilterApplicable_Matches_UserId_Case_Insensitively()
    {
        var policies = new[] { UserPolicy("ABC", p => p.IsAdmin = true) };
        var applicable = AccessPolicyEvaluator.FilterApplicable(policies, "abc", new HashSet<string>());
        applicable.Should().ContainSingle();
    }

    // ---- Admin override ---------------------------------------------------

    [Fact]
    public void Admin_Policy_Grants_Full_Wiki_Access_Without_Explicit_Rules()
    {
        var applicable = new[] { UserPolicy("42", p => p.IsAdmin = true) };

        var access = AccessPolicyEvaluator.EvaluateWikiAccess(applicable, Wiki);

        access.IsAdmin.Should().BeTrue();
        access.CanView.Should().BeTrue();
        access.CanComment.Should().BeTrue();
        access.CanExportWord.Should().BeTrue();
        access.CanExportPdf.Should().BeTrue();
        access.CanUseLetterhead.Should().BeTrue();
    }

    [Fact]
    public void Admin_Policy_Grants_Full_System_Access()
    {
        var applicable = new[] { UserPolicy("42", p => p.IsAdmin = true) };

        var access = AccessPolicyEvaluator.EvaluateSystemAccess(applicable);

        access.IsAdmin.Should().BeTrue();
        access.CanManageWikis.Should().BeTrue();
        access.CanManageUsersAndGroups.Should().BeTrue();
        access.CanManagePermissions.Should().BeTrue();
    }

    // ---- No policy = no access -------------------------------------------

    [Fact]
    public void No_Applicable_Policy_Denies_Everything()
    {
        var access = AccessPolicyEvaluator.EvaluateWikiAccess(Array.Empty<AccessPolicy>(), Wiki);

        access.IsAdmin.Should().BeFalse();
        access.CanView.Should().BeFalse();
        AccessPolicyEvaluator.ToPermissionLevel(access).Should().Be(PermissionLevel.None);
    }

    [Fact]
    public void Rule_For_Different_Wiki_Does_Not_Grant_Access()
    {
        var applicable = new[]
        {
            UserPolicy("42", p => p.Wikis = [Rule(OtherWiki, r => r.CanView = true)])
        };

        var access = AccessPolicyEvaluator.EvaluateWikiAccess(applicable, Wiki);

        access.CanView.Should().BeFalse();
    }

    // ---- OR-combine across rules -----------------------------------------

    [Fact]
    public void Multiple_Rules_For_Same_Wiki_Or_Combine()
    {
        var applicable = new[]
        {
            UserPolicy("42", p => p.Wikis =
            [
                Rule(Wiki, r => r.CanView = true),
                Rule(Wiki, r => r.CanExportPdf = true),
            ]),
            GroupPolicy("7", p => p.Wikis = [Rule(Wiki, r => r.CanExportWord = true)]),
        };

        var access = AccessPolicyEvaluator.EvaluateWikiAccess(applicable, Wiki);

        access.CanView.Should().BeTrue();
        access.CanExportPdf.Should().BeTrue();
        access.CanExportWord.Should().BeTrue();
        access.CanComment.Should().BeFalse("no rule granted comment");
        access.CanUseLetterhead.Should().BeFalse("no rule granted letterhead");
    }

    [Fact]
    public void Wiki_Id_Match_Is_Case_Insensitive()
    {
        var applicable = new[]
        {
            UserPolicy("42", p => p.Wikis = [Rule("WIKI-1", r => r.CanView = true)])
        };

        var access = AccessPolicyEvaluator.EvaluateWikiAccess(applicable, "wiki-1");

        access.CanView.Should().BeTrue();
    }

    // ---- Start points -----------------------------------------------------

    [Fact]
    public void Start_Points_Are_Merged_And_Deduplicated()
    {
        var applicable = new[]
        {
            UserPolicy("42", p => p.Wikis = [Rule(Wiki, r => { r.CanView = true; r.StartPoints = "a|b"; })]),
            GroupPolicy("7", p => p.Wikis = [Rule(Wiki, r => { r.CanView = true; r.StartPoints = "b|c"; })]),
        };

        var access = AccessPolicyEvaluator.EvaluateWikiAccess(applicable, Wiki);
        var points = access.StartPoints.Split('|', StringSplitOptions.RemoveEmptyEntries);

        points.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    // ---- System partial grants -------------------------------------------

    [Fact]
    public void System_Flags_Or_Combine_Without_Admin()
    {
        var applicable = new[]
        {
            UserPolicy("42", p => p.System = new SystemAccessPermissions { ManageWikis = true }),
            GroupPolicy("7", p => p.System = new SystemAccessPermissions { ManagePermissions = true }),
        };

        var access = AccessPolicyEvaluator.EvaluateSystemAccess(applicable);

        access.IsAdmin.Should().BeFalse();
        access.CanManageWikis.Should().BeTrue();
        access.CanManagePermissions.Should().BeTrue();
        access.CanManageUsersAndGroups.Should().BeFalse();
    }

    // ---- PermissionLevel ladder ------------------------------------------

    [Fact]
    public void Level_Is_None_When_Cannot_View()
    {
        var access = new EffectiveWikiAccess { CanView = false };
        AccessPolicyEvaluator.ToPermissionLevel(access).Should().Be(PermissionLevel.None);
    }

    [Fact]
    public void Level_Is_Read_When_View_Only()
    {
        var access = new EffectiveWikiAccess { CanView = true };
        AccessPolicyEvaluator.ToPermissionLevel(access).Should().Be(PermissionLevel.Read);
    }

    [Fact]
    public void Level_Is_Write_When_Can_Comment()
    {
        var access = new EffectiveWikiAccess { CanView = true, CanComment = true };
        AccessPolicyEvaluator.ToPermissionLevel(access).Should().Be(PermissionLevel.Write);
    }

    [Fact]
    public void Level_Is_Admin_When_Admin_Flag_Set()
    {
        var access = new EffectiveWikiAccess { CanView = true, CanComment = true, IsAdmin = true };
        AccessPolicyEvaluator.ToPermissionLevel(access).Should().Be(PermissionLevel.Admin);
    }

    // ---- Wiki access map --------------------------------------------------

    [Fact]
    public void Build_Map_Returns_Empty_For_Admin()
    {
        // Admins are handled separately by the caller; the map is empty by
        // contract so the "which wikis can this non-admin see" path does not
        // accidentally enumerate every wiki for an admin.
        var applicable = new[] { UserPolicy("42", p => p.IsAdmin = true) };

        var map = AccessPolicyEvaluator.BuildWikiAccessMap(applicable);

        map.Should().BeEmpty();
    }

    [Fact]
    public void Build_Map_Aggregates_Per_Wiki()
    {
        var applicable = new[]
        {
            UserPolicy("42", p => p.Wikis =
            [
                Rule(Wiki, r => r.CanView = true),
                Rule(OtherWiki, r => { r.CanView = true; r.CanComment = true; }),
            ]),
        };

        var map = AccessPolicyEvaluator.BuildWikiAccessMap(applicable);

        map.Should().ContainKeys(Wiki, OtherWiki);
        map[Wiki].CanView.Should().BeTrue();
        map[OtherWiki].CanComment.Should().BeTrue();
    }

    [Fact]
    public void Build_Map_Skips_Rules_With_Empty_WikiId()
    {
        var applicable = new[]
        {
            UserPolicy("42", p => p.Wikis = [Rule(string.Empty, r => r.CanView = true)]),
        };

        var map = AccessPolicyEvaluator.BuildWikiAccessMap(applicable);

        map.Should().BeEmpty();
    }

    // ---- ParseStartPoints -------------------------------------------------

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a", 1)]
    [InlineData("a|b|c", 3)]
    [InlineData("a||b", 2)]
    [InlineData(" a | b ", 2)]
    public void ParseStartPoints_Handles_Separators_And_Blanks(string? input, int expectedCount)
    {
        AccessPolicyEvaluator.ParseStartPoints(input).Should().HaveCount(expectedCount);
    }
}
