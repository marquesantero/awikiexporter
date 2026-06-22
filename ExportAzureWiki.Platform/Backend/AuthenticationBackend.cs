using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Data;
using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Services;
using ExportAzureWiki.Services.Authentication;
using ExportAzureWiki.Models.Authentication;
using ExportAzureWiki.Services.Authorization;
using Serilog;
using EntityUser = ExportAzureWiki.Models.Entities.User;

namespace ExportAzureWiki.Platform.Backend;

internal sealed class AuthenticationBackend : IAuthenticationBackend
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly PasswordHashingService _passwordHashingService;
    private readonly OAuthProviderFactoryService _oauthProviderFactoryService;
    private readonly AuthenticationConfigService _authenticationConfigService;
    private readonly AuthorizationService _authorizationService;

    public AuthenticationBackend()
        : this(new DbConnectionFactory(), new PasswordHashingService(), new OAuthProviderFactoryService())
    {
    }

    internal AuthenticationBackend(
        IDbConnectionFactory dbConnectionFactory,
        PasswordHashingService passwordHashingService,
        OAuthProviderFactoryService oauthProviderFactoryService)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _passwordHashingService = passwordHashingService;
        _oauthProviderFactoryService = oauthProviderFactoryService;
        _authenticationConfigService = new AuthenticationConfigService(_dbConnectionFactory);
        _authorizationService = new AuthorizationService();
    }

    public async Task<AuthenticationOutcome> AuthenticateLocalAsync(string usernameOrEmail, string password)
    {
        var normalizedLoginInput = (usernameOrEmail ?? string.Empty).Trim();
        var normalizedPasswordInput = password ?? string.Empty;

        LoggingService.LogInfo($"AUTH_LOCAL attempt login='{normalizedLoginInput}'");
        LoggingService.LogInfo($"AUTH_LOCAL payload userLen={normalizedLoginInput.Length}, passLen={normalizedPasswordInput.Length}");

        if (!await _authenticationConfigService.IsMethodAllowedAsync(ExportAzureWiki.Models.AuthenticationMethod.Local).ConfigureAwait(false))
        {
            LoggingService.LogWarning("AUTH_LOCAL blocked: local authentication is disabled.");
            return AuthenticationOutcome.Failed(AppText.S("auth.local.not_enabled"));
        }

        using var uow = new UnitOfWork(_dbConnectionFactory);
        var normalizedLogin = normalizedLoginInput;
        var user = await uow.Users.GetByUsernameAsync(normalizedLogin).ConfigureAwait(false)
                   ?? await uow.Users.GetByEmailAsync(normalizedLogin).ConfigureAwait(false);

        var snapshot = user == null
            ? null
            : new LocalUserSnapshot(
                user.Id,
                user.Username,
                user.Email,
                user.DisplayName,
                user.PasswordHash,
                user.PasswordSalt,
                user.IsActive);

        if (user != null)
        {
            LoggingService.LogInfo(
                $"AUTH_LOCAL userSnapshot id={user.Id}, authMethod={user.AuthenticationMethod}, hashLen={(user.PasswordHash ?? string.Empty).Length}, saltLen={(user.PasswordSalt ?? string.Empty).Length}");
        }

        if (user != null &&
            user.IsActive &&
            string.IsNullOrWhiteSpace(user.PasswordHash) &&
            string.IsNullOrWhiteSpace(user.PasswordSalt))
        {
            var normalizedPassword = normalizedPasswordInput.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedPassword))
            {
                var repaired = _passwordHashingService.HashPassword(normalizedPassword);
                user.PasswordHash = repaired.hash;
                user.PasswordSalt = repaired.salt;
                user.LastModifiedAt = DateTime.Now;
                await uow.Users.UpdateAsync(user).ConfigureAwait(false);

                snapshot = new LocalUserSnapshot(
                    user.Id,
                    user.Username,
                    user.Email,
                    user.DisplayName,
                    user.PasswordHash,
                    user.PasswordSalt,
                    user.IsActive);

                LoggingService.LogWarning($"AUTH_LOCAL repaired empty password hash/salt for userId={user.Id}, username='{user.Username}'.");
            }
        }

        var decision = LocalAuthenticationRules.Evaluate(
            normalizedLoginInput,
            normalizedPasswordInput,
            snapshot,
            (raw, hash, salt) => _passwordHashingService.VerifyPassword(raw, hash, salt));

        if (!decision.Success || user == null)
        {
            var failureReason = decision.ErrorKey ?? LocalAuthenticationRules.ErrorInvalidUsernamePassword;
            LoggingService.LogWarning(
                $"AUTH_LOCAL failed: reason='{failureReason}', userFound={(user != null)}, userActive={(user?.IsActive ?? false)}");
            return AuthenticationOutcome.Failed(
                AppText.S(failureReason));
        }

        user.LastLoginAt = DateTime.Now;
        user.LastModifiedAt = DateTime.Now;
        await uow.Users.UpdateAsync(user).ConfigureAwait(false);

        LoggingService.LogInfo($"AUTH_LOCAL success userId={user.Id}, username='{user.Username}'");

        return AuthenticationOutcome.Succeeded(BuildAppUser(user, AuthProvider.Local));
    }

    public async Task<AuthenticationOutcome> AuthenticateAzureAsync()
    {
        if (!await _authenticationConfigService.IsMethodAllowedAsync(Models.AuthenticationMethod.AzureAD).ConfigureAwait(false))
        {
            LoggingService.LogWarning("AUTH_AZURE blocked: Azure AD authentication is disabled.");
            return AuthenticationOutcome.Failed(AppText.S("auth.azuread.not_enabled"));
        }

        var provider = await LoadAzureProviderAsync().ConfigureAwait(false);
        if (provider == null)
        {
            return AuthenticationOutcome.Failed(
                AppText.Sf("auth.provider.not_found_or_unconfigured", ExportAzureWiki.Models.Authentication.AuthenticationProvider.AzureAD));
        }

        var providerResult = await provider.AuthenticateAsync().ConfigureAwait(false);
        if (!providerResult.Success || providerResult.User == null)
        {
            return AuthenticationOutcome.Failed(
                providerResult.ErrorMessage ?? AppText.S("auth.error.authenticate_provider"));
        }

        var externalUser = MapExternalProviderUser(providerResult.User);
        var resolveResult = await ResolveExternalUserAsync(externalUser, AuthProvider.AzureAD).ConfigureAwait(false);
        if (!resolveResult.Success || resolveResult.User == null)
        {
            return resolveResult;
        }

        resolveResult.User.AccessToken = providerResult.AccessToken ?? externalUser.AccessToken;
        resolveResult.User.RefreshToken = providerResult.RefreshToken ?? externalUser.RefreshToken;
        resolveResult.User.TokenExpiresAt = providerResult.ExpiresAt ?? externalUser.TokenExpiresAt;

        return resolveResult;
    }

    public async Task SavePreferredLanguageAsync(int userId, string languageCode)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        using var uow = new UnitOfWork(_dbConnectionFactory);
        var user = await uow.Users.GetByIdAsync(userId).ConfigureAwait(false);
        if (user == null)
        {
            return;
        }

        user.PreferredLanguage = languageCode.Trim();
        user.LastModifiedAt = DateTime.Now;
        await uow.Users.UpdateAsync(user).ConfigureAwait(false);
    }

    private async Task<IAuthenticationProvider?> LoadAzureProviderAsync()
    {
        using var uow = new UnitOfWork(_dbConnectionFactory);
        var providers = await uow.OAuthProviders.GetEnabledProvidersAsync().ConfigureAwait(false);
        var azureConfig = providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, "AzureAD", StringComparison.OrdinalIgnoreCase));

        if (azureConfig == null || string.IsNullOrWhiteSpace(azureConfig.ClientId))
        {
            return null;
        }

        try
        {
            return _oauthProviderFactoryService.CreateProvider(azureConfig);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Failed to instantiate Azure AD provider {ProviderName}",
                azureConfig.ProviderName);
            return null;
        }
    }

    private async Task<AuthenticationOutcome> ResolveExternalUserAsync(ExternalProviderUser providerUser, AuthProvider providerType)
    {
        try
        {
            using var uow = new UnitOfWork(_dbConnectionFactory);

            EntityUser? localUser = null;
            var emailCandidate = NormalizeLogin(providerUser.Email);
            var usernameCandidate = NormalizeLogin(providerUser.Username);

            var externalIds = new[]
            {
                NormalizeLogin(providerUser.ObjectId),
                NormalizeLogin(providerUser.ProviderId),
                NormalizeLogin(providerUser.Id)
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            foreach (var externalId in externalIds.Where(_ => localUser == null))
            {
                localUser = await uow.Users.GetByExternalIdAsync(externalId!).ConfigureAwait(false);
                if (localUser != null)
                {
                    break;
                }
            }

            if (localUser == null && !string.IsNullOrWhiteSpace(emailCandidate))
            {
                localUser = await uow.Users.GetByEmailAsync(emailCandidate).ConfigureAwait(false);
                localUser ??= await uow.Users.GetByUsernameAsync(emailCandidate).ConfigureAwait(false);
            }

            if (localUser == null && !string.IsNullOrWhiteSpace(usernameCandidate))
            {
                localUser = await uow.Users.GetByUsernameAsync(usernameCandidate).ConfigureAwait(false);
                localUser ??= await uow.Users.GetByEmailAsync(usernameCandidate).ConfigureAwait(false);
            }

            if (localUser == null && (!string.IsNullOrWhiteSpace(emailCandidate) || !string.IsNullOrWhiteSpace(usernameCandidate)))
            {
                var allUsers = await uow.Users.GetAllAsync().ConfigureAwait(false);
                localUser = allUsers.FirstOrDefault(u =>
                    (!string.IsNullOrWhiteSpace(emailCandidate) && string.Equals(u.Email, emailCandidate, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(emailCandidate) && string.Equals(u.Username, emailCandidate, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(usernameCandidate) && string.Equals(u.Username, usernameCandidate, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(usernameCandidate) && string.Equals(u.Email, usernameCandidate, StringComparison.OrdinalIgnoreCase)));
            }

            if (localUser == null)
            {
                return AuthenticationOutcome.Failed(AppText.S("auth.error.external_user_not_registered"));
            }

            if (!localUser.IsActive)
            {
                return AuthenticationOutcome.Failed(AppText.S("auth.error.user_inactive"));
            }

            var expectedMethod = providerType == AuthProvider.AzureAD
                ? ExportAzureWiki.Models.AuthenticationMethod.AzureAD
                : ExportAzureWiki.Models.AuthenticationMethod.OAuth;

            if (localUser.AuthenticationMethod != expectedMethod)
            {
                return AuthenticationOutcome.Failed(AppText.S("auth.error.external_user_provider_not_allowed"));
            }

            var selectedExternalId = externalIds.FirstOrDefault();
            var hasChanges = false;

            if (!string.IsNullOrWhiteSpace(selectedExternalId) && !string.Equals(localUser.ExternalId, selectedExternalId, StringComparison.OrdinalIgnoreCase))
            {
                localUser.ExternalId = selectedExternalId;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(providerUser.DisplayName) && !string.Equals(localUser.DisplayName, providerUser.DisplayName, StringComparison.Ordinal))
            {
                localUser.DisplayName = providerUser.DisplayName;
                hasChanges = true;
            }

            if (!string.IsNullOrWhiteSpace(emailCandidate) && !string.Equals(localUser.Email, emailCandidate, StringComparison.OrdinalIgnoreCase))
            {
                localUser.Email = emailCandidate;
                hasChanges = true;
            }

            localUser.LastLoginAt = DateTime.Now;
            localUser.LastModifiedAt = DateTime.Now;
            hasChanges = true;

            if (hasChanges)
            {
                await uow.Users.UpdateAsync(localUser).ConfigureAwait(false);
            }

            var effectiveIsAdmin = await ResolveEffectiveIsAdminAsync(localUser).ConfigureAwait(false);
            LoggingService.LogInfo(
                $"AUTH_EXTERNAL resolved userId={localUser.Id}, username='{localUser.Username}', email='{localUser.Email}', effectiveIsAdmin={effectiveIsAdmin}");

            return AuthenticationOutcome.Succeeded(BuildAppUser(localUser, providerType, providerUser));
        }
        catch (Exception ex)
        {
            return AuthenticationOutcome.Failed(AppText.Sf("auth.error.authenticate", ex.Message));
        }
    }

    private async Task<bool> ResolveEffectiveIsAdminAsync(EntityUser localUser)
    {
        var userIdentityCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (localUser.Id > 0)
        {
            userIdentityCandidates.Add(localUser.Id.ToString());
        }

        if (!string.IsNullOrWhiteSpace(localUser.Username))
        {
            userIdentityCandidates.Add(localUser.Username.Trim());
        }

        if (!string.IsNullOrWhiteSpace(localUser.Email))
        {
            userIdentityCandidates.Add(localUser.Email.Trim());
        }

        if (!string.IsNullOrWhiteSpace(localUser.ExternalId))
        {
            userIdentityCandidates.Add(localUser.ExternalId.Trim());
        }

        var groupIdentityCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var uow = new UnitOfWork(_dbConnectionFactory);
            var groups = await uow.Groups.GetByUserIdAsync(localUser.Id).ConfigureAwait(false);
            foreach (var group in groups)
            {
                groupIdentityCandidates.Add(group.Id.ToString());
                if (!string.IsNullOrWhiteSpace(group.Name))
                {
                    groupIdentityCandidates.Add(group.Name.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            // Group lookup failure must NOT silently elevate or demote a
            // permission decision; log explicitly and fall through to
            // direct-policy evaluation so the operator can audit it later.
            Log.Warning(ex,
                "Group lookup failed for user {UserId}; evaluating direct policies only",
                localUser.Id);
        }

        var activePolicies = _authorizationService.GetAccessPolicies();
        var hasAdminPolicy = activePolicies.Any(policy =>
            policy.IsAdmin &&
            (
                (policy.IdentityType == AccessPolicyIdentityType.User &&
                 userIdentityCandidates.Contains(policy.IdentityId)) ||
                (policy.IdentityType == AccessPolicyIdentityType.Group &&
                 groupIdentityCandidates.Contains(policy.IdentityId))
            ));

        return hasAdminPolicy;
    }

    private static AuthenticatedUser BuildAppUser(
        EntityUser localUser,
        AuthProvider providerType,
        ExternalProviderUser? providerUser = null)
    {
        return new AuthenticatedUser
        {
            Id = localUser.Id.ToString(),
            Username = localUser.Username,
            Email = localUser.Email,
            DisplayName = localUser.DisplayName ?? localUser.Username,
            Provider = providerType,
            ProviderId = localUser.ExternalId ?? providerUser?.ProviderId ?? localUser.Id.ToString(),
            IsActive = localUser.IsActive,
            PreferredLanguage = localUser.PreferredLanguage,
            LastLoginAt = DateTime.Now,
            AccessToken = providerUser?.AccessToken,
            RefreshToken = providerUser?.RefreshToken,
            TokenExpiresAt = providerUser?.TokenExpiresAt,
        };
    }

    private static ExternalProviderUser MapExternalProviderUser(ExportAzureWiki.Models.Authentication.User providerUser)
    {
        return new ExternalProviderUser
        {
            Id = providerUser.Id,
            Username = providerUser.Username,
            Email = providerUser.Email,
            DisplayName = providerUser.DisplayName,
            ProviderId = providerUser.ProviderId,
            ObjectId = providerUser.ObjectId,
            AccessToken = providerUser.AccessToken,
            RefreshToken = providerUser.RefreshToken,
            TokenExpiresAt = providerUser.TokenExpiresAt
        };
    }

    private static string? NormalizeLogin(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}





