using ExportAzureWiki.Data;
using ExportAzureWiki.Data.Schema;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services;
using ExportAzureWiki.Services.Authorization;
using EntityUser = ExportAzureWiki.Models.Entities.User;
using AccessPolicyIdentityType = ExportAzureWiki.Models.Authentication.AccessPolicyIdentityType;

namespace ExportAzureWiki.Platform.Setup;

public class SetupService : ISetupService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ISchemaManager _schemaManager;
    private readonly PasswordHashingService _passwordHashingService;

    public SetupService(
        IDbConnectionFactory connectionFactory,
        ISchemaManager schemaManager,
        PasswordHashingService passwordHashingService)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _schemaManager = schemaManager ?? throw new ArgumentNullException(nameof(schemaManager));
        _passwordHashingService = passwordHashingService ?? throw new ArgumentNullException(nameof(passwordHashingService));
    }

    public Task<bool> IsFirstRunAsync()
    {
        return Task.FromResult(SetupChecker.IsFirstRun());
    }

    public async Task<bool> ConfigureDatabaseAsync(DatabaseConfiguration config, Action<string>? progressCallback = null)
    {
        try
        {
            progressCallback?.Invoke("Validating configuration...");

            if (!ConnectionStringBuilder.ValidateConfiguration(config))
            {
                progressCallback?.Invoke("Invalid configuration.");
                return false;
            }

            _connectionFactory.SetConnectionFromConfig(config);

            progressCallback?.Invoke("Checking database...");
            var databaseExists = await _schemaManager.DatabaseExistsAsync().ConfigureAwait(false);

            if (!databaseExists)
            {
                progressCallback?.Invoke("Database not found. Creating...");
                var databaseCreated = await _schemaManager.CreateDatabaseAsync().ConfigureAwait(false);
                if (!databaseCreated)
                {
                    progressCallback?.Invoke("Error creating database.");
                    return false;
                }

                progressCallback?.Invoke("Database created.");
            }
            else
            {
                progressCallback?.Invoke("Database found.");
            }

            progressCallback?.Invoke("Testing connection...");
            if (!await _connectionFactory.TestConnectionAsync().ConfigureAwait(false))
            {
                progressCallback?.Invoke("Connection failed.");
                return false;
            }

            progressCallback?.Invoke("Creating schema...");
            var schemaCreated = await _schemaManager.CreateSchemaAsync().ConfigureAwait(false);
            if (!schemaCreated)
            {
                progressCallback?.Invoke("Error creating schema.");
                return false;
            }

            progressCallback?.Invoke("Seeding OAuth provider templates...");
            await _schemaManager.SeedOAuthProvidersAsync().ConfigureAwait(false);

            progressCallback?.Invoke("Database setup completed.");
            return true;
        }
        catch (Exception ex)
        {
            progressCallback?.Invoke($"Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> CreateAdminUserAsync(string username, string email, string password)
    {
        password = (password ?? string.Empty).Trim();
        if (!_passwordHashingService.ValidatePasswordStrength(password))
        {
            return false;
        }

        var normalizedUsername = (username ?? string.Empty).Trim();
        var normalizedEmail = (email ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new InvalidOperationException("Username is required.");
        }

        using var unitOfWork = new UnitOfWork(_connectionFactory);
        var existingUser = await unitOfWork.Users.GetByUsernameAsync(normalizedUsername).ConfigureAwait(false);
        if (existingUser != null)
        {
            throw new InvalidOperationException("An user with this username already exists.");
        }

        var (hash, salt) = _passwordHashingService.HashPassword(password);
        var user = new EntityUser
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var userId = await unitOfWork.Users.AddAsync(user).ConfigureAwait(false);
        if (userId <= 0)
        {
            return false;
        }

        var authorization = new AuthorizationService();
        var policy = authorization.GetOrCreateAccessPolicy(
            AccessPolicyIdentityType.User,
            userId.ToString(),
            string.IsNullOrWhiteSpace(normalizedEmail) ? normalizedUsername : $"{normalizedUsername} ({normalizedEmail})");
        policy.IsAdmin = true;
        policy.IsActive = true;
        authorization.SaveAccessPolicy(policy);
        return true;
    }

    public Task<bool> CompleteSetupAsync()
    {
        return Task.FromResult(SetupChecker.MarkSetupComplete());
    }

    public async Task<bool> TestConnectionAsync(DatabaseConfiguration config)
    {
        try
        {
            if (!ConnectionStringBuilder.ValidateConfiguration(config))
            {
                return false;
            }

            var connectionString = ConnectionStringBuilder.BuildConnectionString(config);
            var tempFactory = new DbConnectionFactory();
            tempFactory.SetConnection(connectionString, config.DatabaseType);
            return await tempFactory.TestConnectionAsync().ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> AdminUserExistsAsync()
    {
        try
        {
            var connectionString = _connectionFactory.GetConnectionString();
            if (string.IsNullOrEmpty(connectionString))
            {
                return Task.FromResult(false);
            }

            using var unitOfWork = new UnitOfWork(_connectionFactory);
            var authorization = new AuthorizationService();
            return Task.FromResult(authorization.GetAccessPolicies().Any(p => p.IsActive && p.IsAdmin));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public IDbConnectionFactory GetConnectionFactory()
    {
        return _connectionFactory;
    }
}
