using ExportAzureWiki.Models.Authentication;

namespace ExportAzureWiki.Tests.Security;

public sealed class UserSessionTests
{
    [Fact]
    public void Fresh_Session_Has_Random_Session_Id()
    {
        var a = new UserSession();
        var b = new UserSession();

        a.SessionId.Should().NotBeNullOrWhiteSpace();
        b.SessionId.Should().NotBe(a.SessionId, "SessionId rotates on every instance");
    }

    [Fact]
    public void Inactive_User_Always_Invalidates_Session()
    {
        var session = new UserSession
        {
            User = new User { IsActive = false },
            ExpiresAt = DateTime.Now.AddHours(1)
        };

        session.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Past_Absolute_Expiry_Invalidates_Session()
    {
        var session = new UserSession
        {
            User = new User { IsActive = true },
            ExpiresAt = DateTime.Now.AddSeconds(-1)
        };

        session.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Idle_Beyond_Timeout_Invalidates_Session()
    {
        var session = new UserSession
        {
            User = new User { IsActive = true },
            ExpiresAt = DateTime.Now.AddHours(1),
            LastAccessedAt = DateTime.Now.AddMinutes(-61),
            IdleTimeoutMinutes = 60
        };

        session.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Idle_Zero_Disables_Idle_Check()
    {
        var session = new UserSession
        {
            User = new User { IsActive = true },
            ExpiresAt = DateTime.Now.AddHours(1),
            LastAccessedAt = DateTime.Now.AddDays(-7), // very old, should not matter
            IdleTimeoutMinutes = 0
        };

        session.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Touch_Advances_LastAccessedAt()
    {
        var session = new UserSession
        {
            User = new User { IsActive = true },
            ExpiresAt = DateTime.Now.AddHours(1),
            LastAccessedAt = DateTime.Now.AddMinutes(-30),
            IdleTimeoutMinutes = 60
        };

        var before = session.LastAccessedAt;
        Thread.Sleep(10); // make sure clock moved
        session.Touch();

        session.LastAccessedAt.Should().BeAfter(before);
    }
}
