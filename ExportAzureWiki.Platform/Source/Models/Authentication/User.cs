namespace ExportAzureWiki.Models.Authentication
{
    public class User
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public AuthenticationProvider Provider { get; set; }
        public string ProviderId { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public Dictionary<string, string> Claims { get; set; } = new Dictionary<string, string>();
        public List<string> Roles { get; set; } = new List<string>();
        public List<string> Groups { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastLoginAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? TokenExpiresAt { get; set; }

        // Azure AD specific
        public string? TenantId { get; set; }
        public string? ObjectId { get; set; }

        // GitHub specific
        public string? GitHubLogin { get; set; }
        public List<string> GitHubOrganizations { get; set; } = new List<string>();

        // Windows specific
        public string? WindowsSid { get; set; }
        public string? WindowsDomain { get; set; }
    }

    public class UserSession
    {
        /// <summary>
        /// New random identifier on every instance. Login MUST construct a
        /// fresh UserSession (not mutate an existing one) so the SessionId
        /// rotates on every authentication event, defeating session fixation.
        /// </summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        public User User { get; set; } = new User();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Absolute upper bound. The session is invalid after this point even
        /// if the user has been continuously active.
        /// </summary>
        public DateTime ExpiresAt { get; set; } = DateTime.Now.AddHours(24);

        /// <summary>
        /// Last time the session was validated. Used together with
        /// <see cref="IdleTimeoutMinutes"/> to enforce a sliding idle window.
        /// </summary>
        public DateTime LastAccessedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Sliding idle window in minutes. 0 disables the idle check and
        /// falls back to absolute expiry only.
        /// </summary>
        public int IdleTimeoutMinutes { get; set; }

        public bool IsValid
        {
            get
            {
                if (!User.IsActive)
                {
                    return false;
                }

                var now = DateTime.Now;

                if (now >= ExpiresAt)
                {
                    return false;
                }

                if (IdleTimeoutMinutes > 0 && now - LastAccessedAt > TimeSpan.FromMinutes(IdleTimeoutMinutes))
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Slides the idle window forward. Call when a successful operation
        /// shows the user is still active.
        /// </summary>
        public void Touch() => LastAccessedAt = DateTime.Now;
    }
}
