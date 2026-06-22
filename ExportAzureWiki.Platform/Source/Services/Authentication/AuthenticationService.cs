using ExportAzureWiki.Interfaces;
using ExportAzureWiki.Data;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models;
using ExportAzureWiki.Models.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace ExportAzureWiki.Services.Authentication
{
    public class AuthenticationService
    {
        private readonly List<IAuthenticationProvider> _providers;
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private UserSession? _currentSession;
        private readonly AuthenticationConfig _config;

        public event EventHandler<User>? UserLoggedIn;
        public event EventHandler? UserLoggedOut;

        public User? CurrentUser => _currentSession?.User;
        public bool IsAuthenticated => _currentSession?.IsValid == true;

        public AuthenticationService(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _providers = new List<IAuthenticationProvider>();
            _dbConnectionFactory = new DbConnectionFactory();

            _config = LoadConfig();
            LoadSession();
        }

        public void RegisterProvider(IAuthenticationProvider provider)
        {
            if (!_providers.Any(p => p.ProviderType == provider.ProviderType))
            {
                _providers.Add(provider);
            }
        }

        public IEnumerable<IAuthenticationProvider> GetAvailableProviders()
        {
            return _providers.Where(p => p.IsConfigured());
        }

        public async Task<AuthenticationResult> LoginAsync(AuthenticationProvider providerType, Dictionary<string, string>? parameters = null)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
            if (provider == null)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = LocalizationManager.Sf("auth.provider.not_found_or_unconfigured", providerType)
                };
            }

            var result = await provider.AuthenticateAsync(parameters).ConfigureAwait(false);

            if (result.Success && result.User != null)
            {
                if (providerType != AuthenticationProvider.Local)
                {
                    var resolve = await ResolveExternalUserAsync(result.User, providerType).ConfigureAwait(false);
                    if (!resolve.Success || resolve.User == null)
                    {
                        return new AuthenticationResult
                        {
                            Success = false,
                            ErrorMessage = resolve.ErrorMessage ?? LocalizationManager.S("auth.error.external_user_not_registered")
                        };
                    }

                    result.User = resolve.User;
                }

                // Always construct a fresh UserSession so SessionId rotates
                // and any prior session blob in storage is overwritten. This
                // defeats session-fixation: an attacker who somehow planted
                // a known SessionId cannot ride it after the user logs in.
                _currentSession = new UserSession
                {
                    User = result.User,
                    CreatedAt = DateTime.Now,
                    LastAccessedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddMinutes(_config.SessionTimeoutMinutes),
                    IdleTimeoutMinutes = _config.IdleTimeoutMinutes
                };

                SaveSession();
                SaveUser(result.User);
                UserLoggedIn?.Invoke(this, result.User);
            }

            return result;
        }

        private async Task<AuthenticationResult> ResolveExternalUserAsync(User providerUser, AuthenticationProvider providerType)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var unitOfWork = scope.ServiceProvider.GetService<IUnitOfWork>();
                if (unitOfWork == null)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.error.external_registry_unavailable")
                    };
                }

                ExportAzureWiki.Models.Entities.User? localUser = null;
                var emailCandidate = NormalizeLogin(providerUser.Email);
                var usernameCandidate = NormalizeLogin(providerUser.Username);
                // Priority: external login should resolve by e-mail first.
                if (!string.IsNullOrWhiteSpace(emailCandidate))
                {
                    localUser = await unitOfWork.Users.GetByEmailAsync(emailCandidate).ConfigureAwait(false);
                    localUser ??= await unitOfWork.Users.GetByUsernameAsync(emailCandidate).ConfigureAwait(false);
                }

                if (localUser == null && !string.IsNullOrWhiteSpace(usernameCandidate))
                {
                    localUser = await unitOfWork.Users.GetByUsernameAsync(usernameCandidate).ConfigureAwait(false);
                    localUser ??= await unitOfWork.Users.GetByEmailAsync(usernameCandidate).ConfigureAwait(false);
                }

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
                    localUser = await unitOfWork.Users.GetByExternalIdAsync(externalId!).ConfigureAwait(false);
                    if (localUser != null)
                    {
                        break;
                    }
                }

                // Last fallback: in-memory case-insensitive match for edge collations.
                if (localUser == null && (!string.IsNullOrWhiteSpace(emailCandidate) || !string.IsNullOrWhiteSpace(usernameCandidate)))
                {
                    var allUsers = await unitOfWork.Users.GetAllAsync().ConfigureAwait(false);
                    localUser = allUsers.FirstOrDefault(u =>
                        (!string.IsNullOrWhiteSpace(emailCandidate) && string.Equals(u.Email, emailCandidate, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(emailCandidate) && string.Equals(u.Username, emailCandidate, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(usernameCandidate) && string.Equals(u.Username, usernameCandidate, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(usernameCandidate) && string.Equals(u.Email, usernameCandidate, StringComparison.OrdinalIgnoreCase)));
                }

                if (localUser == null)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.error.external_user_not_registered")
                    };
                }

                if (!localUser.IsActive)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.error.user_inactive")
                    };
                }

                var expectedMethod = providerType == AuthenticationProvider.AzureAD
                    ? AuthenticationMethod.AzureAD
                    : AuthenticationMethod.OAuth;

                if (localUser.AuthenticationMethod != expectedMethod)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = LocalizationManager.S("auth.error.external_user_provider_not_allowed")
                    };
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
                    await unitOfWork.Users.UpdateAsync(localUser).ConfigureAwait(false);
                }

                var effectiveIsAdmin = await ResolveEffectiveIsAdminAsync(localUser.Id).ConfigureAwait(false);

                var appUser = new User
                {
                    Id = localUser.Id.ToString(),
                    Username = localUser.Username,
                    Email = localUser.Email,
                    DisplayName = localUser.DisplayName ?? localUser.Username,
                    Provider = providerType,
                    ProviderId = localUser.ExternalId ?? providerUser.ProviderId ?? providerUser.Id,
                    ObjectId = localUser.ExternalId ?? providerUser.ObjectId,
                    IsActive = localUser.IsActive,
                    LastLoginAt = DateTime.Now,
                    AccessToken = providerUser.AccessToken,
                    RefreshToken = providerUser.RefreshToken,
                    TokenExpiresAt = providerUser.TokenExpiresAt,
                    AvatarUrl = providerUser.AvatarUrl,
                    GitHubLogin = providerUser.GitHubLogin,
                    GitHubOrganizations = providerUser.GitHubOrganizations,
                    Groups = providerUser.Groups,
                    Claims = providerUser.Claims,
                    Roles = effectiveIsAdmin ? new List<string> { "Admin" } : new List<string>()
                };

                return new AuthenticationResult
                {
                    Success = true,
                    User = appUser
                };
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    ErrorMessage = LocalizationManager.Sf("auth.error.authenticate", ex.Message)
                };
            }
        }

    private static string? NormalizeLogin(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<bool> ResolveEffectiveIsAdminAsync(int userId)
    {
        try
        {
            using var connection = await _dbConnectionFactory.CreateConnectionAsync().ConfigureAwait(false);
            var policiesTable = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer
                ? "[dbo].[AccessPolicies]"
                : "access_policies";

            var sql = $"""
                       SELECT COUNT(1)
                       FROM {policiesTable}
                       WHERE identity_type = @IdentityType
                         AND identity_id = @IdentityId
                         AND is_admin = @IsAdmin
                         AND is_active = @IsActive
                       """;

            var count = await connection.QuerySingleAsync<int>(sql, new
            {
                IdentityType = 0, // AccessPolicyIdentityType.User
                IdentityId = userId.ToString(),
                IsAdmin = true,
                IsActive = true
            }).ConfigureAwait(false);

            return count > 0;
        }
        catch (Exception ex)
        {
            // A failure here defaults to NOT-admin, which is the safe choice
            // for a permission check. Log explicitly so an operator can spot
            // a DB outage that is silently denying admin privileges to
            // legitimate users instead of just seeing a confusing UI.
            Log.Error(ex,
                "Admin lookup failed for user {UserId}; defaulting to non-admin",
                userId);
            return false;
        }
    }

        public async Task LogoutAsync()
        {
            if (_currentSession?.User != null)
            {
                var provider = _providers.FirstOrDefault(p => p.ProviderType == _currentSession.User.Provider);
                if (provider != null)
                {
                    await provider.SignOutAsync(_currentSession.User).ConfigureAwait(false);
                }
            }

            _currentSession = null;
            ClearSession();
            UserLoggedOut?.Invoke(this, EventArgs.Empty);
        }

        public async Task<bool> ValidateCurrentSessionAsync()
        {
            if (_currentSession == null || !_currentSession.IsValid)
            {
                // Clear the persisted session blob too so an expired session
                // does not linger in the DB waiting for a forced load.
                if (_currentSession != null)
                {
                    _currentSession = null;
                    ClearSession();
                }
                return false;
            }

            var provider = _providers.FirstOrDefault(p => p.ProviderType == _currentSession.User.Provider);
            if (provider == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_currentSession.User.AccessToken))
            {
                var isValid = await provider.ValidateTokenAsync(_currentSession.User.AccessToken).ConfigureAwait(false);
                if (!isValid && !string.IsNullOrEmpty(_currentSession.User.RefreshToken))
                {
                    var newToken = await provider.RefreshTokenAsync(_currentSession.User.RefreshToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        _currentSession.User.AccessToken = newToken;
                        _currentSession.Touch();
                        SaveSession();
                        return true;
                    }
                }

                if (isValid)
                {
                    _currentSession.Touch();
                    SaveSession();
                }
                return isValid;
            }

            _currentSession.Touch();
            SaveSession();
            return true;
        }

        private void SaveSession()
        {
            if (_currentSession == null) return;

            var json = JsonConvert.SerializeObject(_currentSession, Formatting.Indented);
            var encrypted = EncryptionHelper.Encrypt(json);
            UpsertAppSetting("auth.runtime.session", encrypted, isEncrypted: true);
        }

        private void LoadSession()
        {
            try
            {
                var encrypted = GetAppSetting("auth.runtime.session");
                if (string.IsNullOrWhiteSpace(encrypted))
                {
                    return;
                }

                var json = EncryptionHelper.Decrypt(encrypted);
                _currentSession = JsonConvert.DeserializeObject<UserSession>(json);

                if (_currentSession != null && !_currentSession.IsValid)
                {
                    _currentSession = null;
                    ClearSession();
                }
            }
            catch (Exception ex)
            {
                // Failing closed here is intentional: any corruption or
                // tamper attempt on the stored session forces a fresh
                // login. Log so an operator can correlate "everyone got
                // logged out" with the actual cause.
                Log.Warning(ex,
                    "Stored session is unusable, discarding ({ExceptionType})",
                    ex.GetType().Name);
                _currentSession = null;
                ClearSession();
            }
        }

        private void ClearSession()
        {
            DeleteAppSetting("auth.runtime.session");
        }

        private void SaveUser(User user)
        {
            if (!int.TryParse(user.Id, out var userId))
            {
                return;
            }

            try
            {
                using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
                var table = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer ? "[dbo].[Users]" : "users";
                var idCol = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer ? "Id" : "id";
                var loginCol = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer ? "LastLoginAt" : "last_login_at";
                var modifiedCol = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer ? "LastModifiedAt" : "last_modified_at";
                var emailCol = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer ? "Email" : "email";
                var displayCol = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer ? "DisplayName" : "display_name";
                var externalCol = _dbConnectionFactory.GetDatabaseType() == DatabaseType.SqlServer ? "ExternalId" : "external_id";

                var sql = $"""
                           UPDATE {table}
                           SET {loginCol} = @LastLoginAt,
                               {modifiedCol} = @LastModifiedAt,
                               {emailCol} = @Email,
                               {displayCol} = @DisplayName,
                               {externalCol} = @ExternalId
                           WHERE {idCol} = @Id
                           """;
                connection.Execute(sql, new
                {
                    Id = userId,
                    LastLoginAt = user.LastLoginAt,
                    LastModifiedAt = DateTime.Now,
                    user.Email,
                    user.DisplayName,
                    ExternalId = user.ObjectId ?? user.ProviderId
                });
            }
            catch (Exception ex)
            {
                // Best effort update -- a failure to record the login
                // metadata must not block the user from signing in -- but
                // silently dropping it makes incident response impossible.
                Log.Warning(ex,
                    "Could not update last-login metadata for user {UserId}",
                    userId);
            }
        }

        private AuthenticationConfig LoadConfig()
        {
            var json = GetAppSetting("auth.runtime.config");
            return string.IsNullOrWhiteSpace(json)
                ? new AuthenticationConfig()
                : JsonConvert.DeserializeObject<AuthenticationConfig>(json) ?? new AuthenticationConfig();
        }

        public void SaveConfig(AuthenticationConfig config)
        {
            config ??= new AuthenticationConfig();
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            UpsertAppSetting("auth.runtime.config", json, isEncrypted: false);

            // During constructor bootstrap, _config is not assigned yet.
            if (_config == null)
            {
                return;
            }

            _config.AllowMultipleProviders = config.AllowMultipleProviders;
            _config.RequireAuthentication = config.RequireAuthentication;
            _config.SessionTimeoutMinutes = config.SessionTimeoutMinutes;
            _config.IdleTimeoutMinutes = config.IdleTimeoutMinutes;
            _config.EnableRememberMe = config.EnableRememberMe;
        }

        public AuthenticationConfig GetConfig() => _config;

        private string? GetAppSetting(string key)
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
                var dbType = _dbConnectionFactory.GetDatabaseType();
                var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";
                var sql = dbType switch
                {
                    DatabaseType.SqlServer => $"SELECT [Value] FROM {table} WHERE [Key] = @Key",
                    DatabaseType.MySQL => $"SELECT value FROM {table} WHERE `key` = @Key",
                    _ => $"SELECT value FROM {table} WHERE key = @Key"
                };

                return connection.QueryFirstOrDefault<string>(sql, new { Key = key });
            }
            catch (Exception ex)
            {
                // A missing setting is normal on first run, but a DB error
                // here looks identical to the caller. Surface the cause for
                // diagnostics without changing the return contract.
                Log.Warning(ex,
                    "App-setting lookup failed for key {SettingKey}",
                    key);
                return null;
            }
        }

        private void UpsertAppSetting(string key, string value, bool isEncrypted)
        {
            using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
            var dbType = _dbConnectionFactory.GetDatabaseType();
            var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";
            if (dbType == DatabaseType.SqlServer)
            {
                connection.Execute(
                    $"""
                     MERGE {table} AS target
                     USING (SELECT @Key AS [Key]) AS source
                     ON target.[Key] = source.[Key]
                     WHEN MATCHED THEN
                         UPDATE SET [Value] = @Value, [IsEncrypted] = @IsEncrypted, [LastModifiedAt] = GETDATE()
                     WHEN NOT MATCHED THEN
                         INSERT ([Key], [Value], [IsEncrypted], [LastModifiedAt])
                         VALUES (@Key, @Value, @IsEncrypted, GETDATE());
                     """,
                    new { Key = key, Value = value, IsEncrypted = isEncrypted });
                return;
            }

            if (dbType == DatabaseType.MySQL)
            {
                connection.Execute(
                    $"""
                     INSERT INTO {table} (`key`, value, is_encrypted, last_modified_at)
                     VALUES (@Key, @Value, @IsEncrypted, CURRENT_TIMESTAMP)
                     ON DUPLICATE KEY UPDATE
                         value = VALUES(value),
                         is_encrypted = VALUES(is_encrypted),
                         last_modified_at = CURRENT_TIMESTAMP
                     """,
                    new { Key = key, Value = value, IsEncrypted = isEncrypted });
            }
            else
            {
                connection.Execute(
                    $"""
                     INSERT INTO {table} (key, value, is_encrypted, last_modified_at)
                     VALUES (@Key, @Value, @IsEncrypted, CURRENT_TIMESTAMP)
                     ON CONFLICT(key) DO UPDATE SET
                         value = excluded.value,
                         is_encrypted = excluded.is_encrypted,
                         last_modified_at = CURRENT_TIMESTAMP
                     """,
                    new { Key = key, Value = value, IsEncrypted = isEncrypted });
            }
        }

        private void DeleteAppSetting(string key)
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
                var dbType = _dbConnectionFactory.GetDatabaseType();
                var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";
                var sql = dbType switch
                {
                    DatabaseType.SqlServer => $"DELETE FROM {table} WHERE [Key] = @Key",
                    DatabaseType.MySQL => $"DELETE FROM {table} WHERE `key` = @Key",
                    _ => $"DELETE FROM {table} WHERE key = @Key"
                };

                connection.Execute(sql, new { Key = key });
            }
            catch (Exception ex)
            {
                // Cleanup failure: the session may linger encrypted in the
                // DB. The next LoadSession will treat it as unusable, but
                // log so the operator can spot the underlying DB issue.
                Log.Warning(ex,
                    "Failed to remove app-setting key {SettingKey}",
                    key);
            }
        }
    }
}
