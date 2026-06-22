using System.Data;
using System.Reflection;
using Dapper;
using ExportAzureWiki.Models;
using ExportAzureWiki.Platform.Data;

namespace ExportAzureWiki.Data.Schema;

/// <summary>
/// Manages database schema creation and versioning
/// </summary>
public class SchemaManager : ISchemaManager
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SchemaManager(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc/>
    public async Task<bool> DatabaseExistsAsync()
    {
        try
        {
            var databaseType = _connectionFactory.GetDatabaseType();

            // SQLite always exists (file-based)
            if (databaseType == DatabaseType.SQLite)
            {
                return true;
            }

            // For server-based databases, check if database exists
            var config = _connectionFactory.LoadConfiguration();
            if (config == null)
            {
                return false;
            }

            // The database name is validated up front so subsequent uses
            // (CREATE DATABASE etc.) can rely on it. Even though this lookup
            // uses a parameter, allowing arbitrary strings into config opens
            // problems elsewhere; fail fast here.
            var databaseName = SqlIdentifier.Validate(config.Database);
            using var connection = await CreateMasterConnectionAsync(config).ConfigureAwait(false);

            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "SELECT COUNT(*) FROM sys.databases WHERE name = @Name",
                DatabaseType.PostgreSQL => "SELECT COUNT(*) FROM pg_database WHERE datname = @Name",
                DatabaseType.MySQL => "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @Name",
                _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
            };

            var count = await connection.QuerySingleAsync<int>(sql, new { Name = databaseName }).ConfigureAwait(false);
            return count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking database existence: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CreateDatabaseAsync()
    {
        try
        {
            var databaseType = _connectionFactory.GetDatabaseType();

            // SQLite is file-based, created automatically on connection
            if (databaseType == DatabaseType.SQLite)
            {
                return true;
            }

            var config = _connectionFactory.LoadConfiguration();
            if (config == null)
            {
                throw new InvalidOperationException("Configuração do banco de dados não encontrada");
            }

            // CREATE DATABASE cannot accept a parameter for the name in any
            // major dialect, so the only safe path is allow-list + provider
            // quoting on a value that has already passed validation.
            var quotedDatabaseName = SqlIdentifier.Quote(config.Database, databaseType);

            System.Diagnostics.Debug.WriteLine($"Attempting to create database: {quotedDatabaseName}");

            using var connection = await CreateMasterConnectionAsync(config).ConfigureAwait(false);

            var sql = databaseType switch
            {
                DatabaseType.SqlServer => $"CREATE DATABASE {quotedDatabaseName}",
                DatabaseType.PostgreSQL => $"CREATE DATABASE {quotedDatabaseName}",
                DatabaseType.MySQL => $"CREATE DATABASE {quotedDatabaseName} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
                _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
            };

            System.Diagnostics.Debug.WriteLine($"Executing SQL: {sql}");

            await connection.ExecuteAsync(sql).ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"Database created successfully: {quotedDatabaseName}");

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating database: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

            // Re-throw to let caller handle it
            throw new InvalidOperationException($"Falha ao criar banco de dados: {ex.Message}", ex);
        }
    }

    private async Task<IDbConnection> CreateMasterConnectionAsync(Models.DatabaseConfiguration config)
    {
        try
        {
            var databaseType = config.DatabaseType;

            System.Diagnostics.Debug.WriteLine($"Creating master connection for {databaseType}");
            System.Diagnostics.Debug.WriteLine($"Server: {config.Server}:{config.Port}");
            System.Diagnostics.Debug.WriteLine($"UseWindowsAuth: {config.UseWindowsAuth}");
            System.Diagnostics.Debug.WriteLine($"Username: {config.Username ?? "(null)"}");

            // Create connection to master/default database
            var masterConfig = new Models.DatabaseConfiguration
            {
                DatabaseType = config.DatabaseType,
                Server = config.Server,
                Port = config.Port,
                Username = config.Username,
                Password = config.Password,
                UseWindowsAuth = config.UseWindowsAuth,
                // Carry the TLS trust setting to the master/default connection too;
                // otherwise creating the database fails cert validation even when
                // the user opted into TrustServerCertificate.
                TrustServerCertificate = config.TrustServerCertificate,
                Database = databaseType switch
                {
                    DatabaseType.SqlServer => "master",
                    DatabaseType.PostgreSQL => "postgres",
                    DatabaseType.MySQL => "mysql",
                    _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
                }
            };

            var connectionString = ConnectionStringBuilder.BuildConnectionString(masterConfig);

            System.Diagnostics.Debug.WriteLine($"Connection string (without password): {connectionString.Replace(config.Password ?? "", "***")}");

            IDbConnection connection;

            switch (databaseType)
            {
                case DatabaseType.SqlServer:
                    System.Diagnostics.Debug.WriteLine("Opening SQL Server connection to master...");
                    var sqlConn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                    await sqlConn.OpenAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("SQL Server connection opened successfully");
                    connection = sqlConn;
                    break;

                case DatabaseType.PostgreSQL:
                    System.Diagnostics.Debug.WriteLine("Opening PostgreSQL connection to postgres...");
                    var npgsqlConn = new Npgsql.NpgsqlConnection(connectionString);
                    await npgsqlConn.OpenAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("PostgreSQL connection opened successfully");
                    connection = npgsqlConn;
                    break;

                case DatabaseType.MySQL:
                    System.Diagnostics.Debug.WriteLine("Opening MySQL connection to mysql...");
                    var mysqlConn = new MySqlConnector.MySqlConnection(connectionString);
                    await mysqlConn.OpenAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine("MySQL connection opened successfully");
                    connection = mysqlConn;
                    break;

                default:
                    throw new NotSupportedException($"Database type {databaseType} is not supported");
            }

            return connection;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating master connection: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException?.Message}");
            throw new InvalidOperationException($"Não foi possível conectar ao banco de dados master/padrão. Verifique suas credenciais e permissões.\n\nDetalhes: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CreateSchemaAsync()
    {
        try
        {
            var databaseType = _connectionFactory.GetDatabaseType();
            var scriptContent = GetSchemaScript(databaseType);

            if (string.IsNullOrWhiteSpace(scriptContent))
            {
                throw new InvalidOperationException($"Schema script not found for database type: {databaseType}");
            }

            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);

            // Split script by GO (SQL Server) or ; (other databases) and execute each batch
            var batches = SplitScriptIntoBatches(scriptContent, databaseType);

            foreach (var batch in batches)
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await connection.ExecuteAsync(batch).ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating schema: {ex.Message}");
            throw new InvalidOperationException("Failed to create database schema", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SchemaExistsAsync()
    {
        try
        {
            var version = await GetSchemaVersionAsync().ConfigureAwait(false);
            return version > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<int> GetSchemaVersionAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);

            var databaseType = _connectionFactory.GetDatabaseType();
            var tableName = GetTableName("schema_version", databaseType);

            // TableExistsAsync matches INFORMATION_SCHEMA by the *unqualified*
            // table name, which on SQL Server is "SchemaVersion" (PascalCase, no
            // underscore) -- not the logical "schema_version". Passing the raw
            // logical name made SQL Server always report version 0 (schema "not
            // present"), unlike the other engines.
            var versionTableName = databaseType == DatabaseType.SqlServer ? "SchemaVersion" : "schema_version";
            var tableExists = await TableExistsAsync(connection, versionTableName).ConfigureAwait(false);
            if (!tableExists)
            {
                return 0;
            }

            var sql = $"SELECT MAX(version) FROM {tableName}";
            var version = await connection.QuerySingleOrDefaultAsync<int?>(sql).ConfigureAwait(false);

            return version ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateSchemaAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);

            // Check if essential tables exist
            var databaseType = _connectionFactory.GetDatabaseType();
            var essentialTables = databaseType switch
            {
                DatabaseType.SqlServer => new[]
                {
                    "Users", "OAuthProviders", "AiProviders", "WikiConfigurations", "AuthenticationConfiguration", "AccessPolicies", "AccessPolicyWikis"
                },
                _ => new[]
                {
                    "users", "oauth_providers", "ai_providers", "wiki_configurations", "authentication_configuration", "access_policies", "access_policy_wikis"
                }
            };

            foreach (var table in essentialTables)
            {
                if (!await TableExistsAsync(connection, table).ConfigureAwait(false))
                {
                    return false;
                }
            }

            // Check if schema version is current
            var currentVersion = await GetSchemaVersionAsync().ConfigureAwait(false);
            return currentVersion >= 1;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> EnsureRequiredTablesAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
            var databaseType = _connectionFactory.GetDatabaseType();

            // Each incremental structural change runs through the migration
            // journal: the runner skips an id already recorded in
            // schema_migrations, so on an existing database the idempotent
            // Ensure* step runs once (finds nothing to do) and is then
            // journaled; on a fresh database it creates and is journaled;
            // subsequent boots skip it entirely. This gives an auditable
            // record of which upgrades a given database has received
            // without ripping out the working idempotent bootstrap. See
            // Fase 3.4.
            var runner = new SchemaMigrationRunner(connection, databaseType);
            var migrations = BuildMigrations(databaseType);
            await runner.RunAsync(migrations).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            // Schema upgrade failing means the app may run against a stale
            // schema and throw "column does not exist" later. Log loudly so
            // the cause is visible at boot, not just in a debugger.
            Serilog.Log.Error(ex, "Schema migration run failed while ensuring required tables");
            return false;
        }
    }

    /// <summary>
    /// The ordered list of incremental structural migrations. Each migration
    /// id is stable and must never change once shipped. The work inside each
    /// step is the existing idempotent Ensure* helper, so a migration is safe
    /// to re-run if it was applied before the journal existed.
    /// </summary>
    private List<SchemaMigration> BuildMigrations(DatabaseType databaseType)
    {
        var migrations = new List<SchemaMigration>
        {
            new("0001_authentication_configuration", "Authentication configuration table", async c =>
            {
                var table = databaseType == DatabaseType.SqlServer ? "AuthenticationConfiguration" : "authentication_configuration";
                if (!await TableExistsAsync(c, table).ConfigureAwait(false))
                {
                    await EnsureAuthenticationConfigurationTableAsync(c, databaseType).ConfigureAwait(false);
                }
            }),

            new("0002_user_auth_columns", "User auth columns (display name, method, external id)", async c =>
            {
                var usersTable = databaseType == DatabaseType.SqlServer ? "Users" : "users";
                if (await TableExistsAsync(c, usersTable).ConfigureAwait(false))
                {
                    await EnsureUserAuthColumnsAsync(c, databaseType).ConfigureAwait(false);
                }
            }),

            new("0003_identity_group_columns", "Identity group columns (is_system, source)", async c =>
            {
                var groupsTable = databaseType == DatabaseType.SqlServer ? "IdentityGroups" : "identity_groups";
                if (await TableExistsAsync(c, groupsTable).ConfigureAwait(false))
                {
                    await EnsureIdentityGroupColumnsAsync(c, databaseType).ConfigureAwait(false);
                }
            }),

            new("0004_ai_providers", "AI providers table", async c =>
            {
                var table = databaseType == DatabaseType.SqlServer ? "AiProviders" : "ai_providers";
                if (!await TableExistsAsync(c, table).ConfigureAwait(false))
                {
                    await EnsureAiProvidersTableAsync(c, databaseType).ConfigureAwait(false);
                }
            }),

            new("0005_access_policies", "Access policies and per-wiki rules", async c =>
            {
                var accessPoliciesTable = databaseType == DatabaseType.SqlServer ? "AccessPolicies" : "access_policies";
                if (!await TableExistsAsync(c, accessPoliciesTable).ConfigureAwait(false))
                {
                    await EnsureAccessPoliciesTablesAsync(c, databaseType).ConfigureAwait(false);
                    return;
                }

                var accessPolicyWikisTable = databaseType == DatabaseType.SqlServer ? "AccessPolicyWikis" : "access_policy_wikis";
                if (!await TableExistsAsync(c, accessPolicyWikisTable).ConfigureAwait(false))
                {
                    await EnsureAccessPoliciesTablesAsync(c, databaseType).ConfigureAwait(false);
                }
                else if (!await ColumnExistsAsync(c, accessPolicyWikisTable, "can_comment").ConfigureAwait(false))
                {
                    var alterSql = databaseType switch
                    {
                        DatabaseType.SqlServer => "ALTER TABLE [dbo].[AccessPolicyWikis] ADD [can_comment] BIT NOT NULL CONSTRAINT DF_AccessPolicyWikis_CanComment DEFAULT(0)",
                        DatabaseType.PostgreSQL => "ALTER TABLE access_policy_wikis ADD COLUMN can_comment BOOLEAN NOT NULL DEFAULT FALSE",
                        DatabaseType.MySQL => "ALTER TABLE access_policy_wikis ADD COLUMN can_comment BOOLEAN NOT NULL DEFAULT FALSE",
                        DatabaseType.SQLite => "ALTER TABLE access_policy_wikis ADD COLUMN can_comment INTEGER NOT NULL DEFAULT 0",
                        _ => throw new NotSupportedException($"Database type {databaseType} not supported")
                    };
                    await c.ExecuteAsync(alterSql).ConfigureAwait(false);
                }
            }),

            new("0006_wiki_configuration_ownership", "Wiki configuration ownership columns", async c =>
            {
                var table = databaseType == DatabaseType.SqlServer ? "WikiConfigurations" : "wiki_configurations";
                if (await TableExistsAsync(c, table).ConfigureAwait(false))
                {
                    await EnsureWikiConfigurationOwnershipColumnsAsync(c, databaseType).ConfigureAwait(false);
                }
            }),

            new("0007_user_preferred_language", "Preferred language column on users", async c =>
            {
                await EnsureSchemaVersion2PreferredLanguageAsync(c, databaseType).ConfigureAwait(false);
            }),

            new("0008_security_audit_log", "Security audit log table", async c =>
            {
                var table = databaseType == DatabaseType.SqlServer ? "SecurityAuditLog" : "security_audit_log";
                if (!await TableExistsAsync(c, table).ConfigureAwait(false))
                {
                    await EnsureSecurityAuditLogTableAsync(c, databaseType).ConfigureAwait(false);
                }
            }),
        };

        return migrations;
    }

    private static async Task EnsureAuthenticationConfigurationTableAsync(IDbConnection connection, DatabaseType databaseType)
    {
        var sql = databaseType switch
        {
            DatabaseType.SqlServer => @"
CREATE TABLE [dbo].[AuthenticationConfiguration] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [PrimaryMethod] INT NOT NULL DEFAULT 0,
    [AllowWindowsAuth] BIT NOT NULL DEFAULT 0,
    [AllowAzureAD] BIT NOT NULL DEFAULT 0,
    [AllowLocalAuth] BIT NOT NULL DEFAULT 1,
    [RequireAuthentication] BIT NOT NULL DEFAULT 1,
    [SyncAzureADGroups] BIT NOT NULL DEFAULT 0,
    [SyncWindowsGroups] BIT NOT NULL DEFAULT 0,
    [AzureADTenantId] NVARCHAR(512),
    [AutoCreateUsers] BIT NOT NULL DEFAULT 0,
    [DefaultRole] NVARCHAR(100) NOT NULL DEFAULT 'User',
    [UseLocalPermissions] BIT NOT NULL DEFAULT 1,
    [UseAzureADPermissions] BIT NOT NULL DEFAULT 0,
    [UseWindowsPermissions] BIT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [UpdatedAt] DATETIME2
);",
            DatabaseType.SQLite => @"
CREATE TABLE IF NOT EXISTS authentication_configuration (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    primary_method INTEGER NOT NULL DEFAULT 0,
    allow_windows_auth INTEGER NOT NULL DEFAULT 0,
    allow_azure_ad INTEGER NOT NULL DEFAULT 0,
    allow_local_auth INTEGER NOT NULL DEFAULT 1,
    require_authentication INTEGER NOT NULL DEFAULT 1,
    sync_azure_ad_groups INTEGER NOT NULL DEFAULT 0,
    sync_windows_groups INTEGER NOT NULL DEFAULT 0,
    azure_ad_tenant_id TEXT,
    auto_create_users INTEGER NOT NULL DEFAULT 0,
    default_role TEXT NOT NULL DEFAULT 'User',
    use_local_permissions INTEGER NOT NULL DEFAULT 1,
    use_azure_ad_permissions INTEGER NOT NULL DEFAULT 0,
    use_windows_permissions INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT
);",
            DatabaseType.PostgreSQL => @"
CREATE TABLE IF NOT EXISTS authentication_configuration (
    id SERIAL PRIMARY KEY,
    primary_method INTEGER NOT NULL DEFAULT 0,
    allow_windows_auth BOOLEAN NOT NULL DEFAULT FALSE,
    allow_azure_ad BOOLEAN NOT NULL DEFAULT FALSE,
    allow_local_auth BOOLEAN NOT NULL DEFAULT TRUE,
    require_authentication BOOLEAN NOT NULL DEFAULT TRUE,
    sync_azure_ad_groups BOOLEAN NOT NULL DEFAULT FALSE,
    sync_windows_groups BOOLEAN NOT NULL DEFAULT FALSE,
    azure_ad_tenant_id VARCHAR(512),
    auto_create_users BOOLEAN NOT NULL DEFAULT FALSE,
    default_role VARCHAR(100) NOT NULL DEFAULT 'User',
    use_local_permissions BOOLEAN NOT NULL DEFAULT TRUE,
    use_azure_ad_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    use_windows_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP
);",
            DatabaseType.MySQL => @"
CREATE TABLE IF NOT EXISTS authentication_configuration (
    id INT AUTO_INCREMENT PRIMARY KEY,
    primary_method INT NOT NULL DEFAULT 0,
    allow_windows_auth BOOLEAN NOT NULL DEFAULT FALSE,
    allow_azure_ad BOOLEAN NOT NULL DEFAULT FALSE,
    allow_local_auth BOOLEAN NOT NULL DEFAULT TRUE,
    require_authentication BOOLEAN NOT NULL DEFAULT TRUE,
    sync_azure_ad_groups BOOLEAN NOT NULL DEFAULT FALSE,
    sync_windows_groups BOOLEAN NOT NULL DEFAULT FALSE,
    azure_ad_tenant_id VARCHAR(512),
    auto_create_users BOOLEAN NOT NULL DEFAULT FALSE,
    default_role VARCHAR(100) NOT NULL DEFAULT 'User',
    use_local_permissions BOOLEAN NOT NULL DEFAULT TRUE,
    use_azure_ad_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    use_windows_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private static async Task EnsureSecurityAuditLogTableAsync(IDbConnection connection, DatabaseType databaseType)
    {
        // Fase 4.3 audit log. The table is append-only from the
        // application's perspective; manual retention is an ops decision.
        var sql = databaseType switch
        {
            DatabaseType.SqlServer => """
                CREATE TABLE [dbo].[SecurityAuditLog] (
                    [Id]         BIGINT IDENTITY(1,1) PRIMARY KEY,
                    [OccurredAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    [EventType]  NVARCHAR(64) NOT NULL,
                    [UserId]     INT NULL,
                    [Username]   NVARCHAR(255) NULL,
                    [IpAddress]  NVARCHAR(64) NULL,
                    [UserAgent]  NVARCHAR(512) NULL,
                    [Detail]     NVARCHAR(MAX) NULL
                );
                CREATE INDEX IX_SecurityAuditLog_OccurredAt ON [dbo].[SecurityAuditLog]([OccurredAt] DESC);
                CREATE INDEX IX_SecurityAuditLog_EventType ON [dbo].[SecurityAuditLog]([EventType], [OccurredAt] DESC);
                CREATE INDEX IX_SecurityAuditLog_UserId ON [dbo].[SecurityAuditLog]([UserId], [OccurredAt] DESC);
                """,
            DatabaseType.PostgreSQL => """
                CREATE TABLE IF NOT EXISTS security_audit_log (
                    id          BIGSERIAL PRIMARY KEY,
                    occurred_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    event_type  VARCHAR(64) NOT NULL,
                    user_id     INTEGER NULL,
                    username    VARCHAR(255) NULL,
                    ip_address  VARCHAR(64) NULL,
                    user_agent  VARCHAR(512) NULL,
                    detail      TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_security_audit_log_occurred_at ON security_audit_log(occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_security_audit_log_event_type ON security_audit_log(event_type, occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_security_audit_log_user_id ON security_audit_log(user_id, occurred_at DESC);
                """,
            DatabaseType.MySQL => """
                CREATE TABLE IF NOT EXISTS security_audit_log (
                    id          BIGINT AUTO_INCREMENT PRIMARY KEY,
                    occurred_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    event_type  VARCHAR(64) NOT NULL,
                    user_id     INT NULL,
                    username    VARCHAR(255) NULL,
                    ip_address  VARCHAR(64) NULL,
                    user_agent  VARCHAR(512) NULL,
                    detail      TEXT NULL,
                    INDEX ix_security_audit_log_occurred_at (occurred_at DESC),
                    INDEX ix_security_audit_log_event_type (event_type, occurred_at DESC),
                    INDEX ix_security_audit_log_user_id (user_id, occurred_at DESC)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
                """,
            DatabaseType.SQLite => """
                CREATE TABLE IF NOT EXISTS security_audit_log (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    occurred_at TEXT NOT NULL DEFAULT (datetime('now')),
                    event_type  TEXT NOT NULL,
                    user_id     INTEGER NULL,
                    username    TEXT NULL,
                    ip_address  TEXT NULL,
                    user_agent  TEXT NULL,
                    detail      TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_security_audit_log_occurred_at ON security_audit_log(occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_security_audit_log_event_type ON security_audit_log(event_type, occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_security_audit_log_user_id ON security_audit_log(user_id, occurred_at DESC);
                """,
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };

        // SQL Server's CREATE TABLE + CREATE INDEX in a single ExecuteAsync
        // call gets batched fine by SqlClient; for SQLite the three
        // statements need to be issued one at a time.
        if (databaseType == DatabaseType.SQLite)
        {
            foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (statement.Length > 0)
                {
                    await connection.ExecuteAsync(statement).ConfigureAwait(false);
                }
            }
            return;
        }

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private async Task EnsureUserAuthColumnsAsync(IDbConnection connection, DatabaseType databaseType)
    {
        var tableName = databaseType == DatabaseType.SqlServer ? "Users" : "users";

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "DisplayName" : "display_name").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[Users] ADD [DisplayName] NVARCHAR(255) NULL",
                DatabaseType.PostgreSQL => "ALTER TABLE users ADD COLUMN display_name VARCHAR(255)",
                DatabaseType.MySQL => "ALTER TABLE users ADD COLUMN display_name VARCHAR(255) NULL",
                _ => "ALTER TABLE users ADD COLUMN display_name TEXT"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "AuthenticationMethod" : "authentication_method").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[Users] ADD [AuthenticationMethod] INT NULL",
                DatabaseType.PostgreSQL => "ALTER TABLE users ADD COLUMN authentication_method INTEGER",
                DatabaseType.MySQL => "ALTER TABLE users ADD COLUMN authentication_method INT NULL",
                _ => "ALTER TABLE users ADD COLUMN authentication_method INTEGER NULL"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "ExternalId" : "external_id").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[Users] ADD [ExternalId] NVARCHAR(255) NULL",
                DatabaseType.PostgreSQL => "ALTER TABLE users ADD COLUMN external_id VARCHAR(255)",
                DatabaseType.MySQL => "ALTER TABLE users ADD COLUMN external_id VARCHAR(255) NULL",
                _ => "ALTER TABLE users ADD COLUMN external_id TEXT NULL"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        // Fase 4.2: brute-force protection columns.
        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "FailedLoginCount" : "failed_login_count").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[Users] ADD [FailedLoginCount] INT NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT(0)",
                DatabaseType.PostgreSQL => "ALTER TABLE users ADD COLUMN failed_login_count INTEGER NOT NULL DEFAULT 0",
                DatabaseType.MySQL => "ALTER TABLE users ADD COLUMN failed_login_count INT NOT NULL DEFAULT 0",
                _ => "ALTER TABLE users ADD COLUMN failed_login_count INTEGER NOT NULL DEFAULT 0"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "LockedUntil" : "locked_until").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[Users] ADD [LockedUntil] DATETIME2 NULL",
                DatabaseType.PostgreSQL => "ALTER TABLE users ADD COLUMN locked_until TIMESTAMP NULL",
                DatabaseType.MySQL => "ALTER TABLE users ADD COLUMN locked_until DATETIME NULL",
                _ => "ALTER TABLE users ADD COLUMN locked_until TEXT NULL"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }
    }

    private async Task EnsureIdentityGroupColumnsAsync(IDbConnection connection, DatabaseType databaseType)
    {
        var tableName = databaseType == DatabaseType.SqlServer ? "IdentityGroups" : "identity_groups";

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "IsSystem" : "is_system").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[IdentityGroups] ADD [IsSystem] BIT NOT NULL DEFAULT 0",
                DatabaseType.PostgreSQL => "ALTER TABLE identity_groups ADD COLUMN is_system BOOLEAN NOT NULL DEFAULT FALSE",
                DatabaseType.MySQL => "ALTER TABLE identity_groups ADD COLUMN is_system BOOLEAN NOT NULL DEFAULT FALSE",
                _ => "ALTER TABLE identity_groups ADD COLUMN is_system INTEGER NOT NULL DEFAULT 0"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "Source" : "source").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[IdentityGroups] ADD [Source] NVARCHAR(50) NULL",
                DatabaseType.PostgreSQL => "ALTER TABLE identity_groups ADD COLUMN source VARCHAR(50)",
                DatabaseType.MySQL => "ALTER TABLE identity_groups ADD COLUMN source VARCHAR(50) NULL",
                _ => "ALTER TABLE identity_groups ADD COLUMN source TEXT"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }
    }

    private async Task<bool> TableExistsAsync(IDbConnection connection, string tableName)
    {
        var databaseType = _connectionFactory.GetDatabaseType();

        var sql = databaseType switch
        {
            DatabaseType.SqlServer => $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'",
            DatabaseType.PostgreSQL => $"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '{tableName}'",
            DatabaseType.MySQL => $"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '{tableName}' AND table_schema = DATABASE()",
            DatabaseType.SQLite => $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };

        var count = await connection.QuerySingleAsync<int>(sql).ConfigureAwait(false);
        return count > 0;
    }

    private async Task<bool> ColumnExistsAsync(IDbConnection connection, string tableName, string columnName)
    {
        var databaseType = _connectionFactory.GetDatabaseType();

        if (databaseType == DatabaseType.SQLite)
        {
            var escapedTableName = tableName.Replace("'", "''");
            var sql = $"SELECT COUNT(*) FROM pragma_table_info('{escapedTableName}') WHERE name = @ColumnName";
            var sqliteCount = await connection.QuerySingleAsync<int>(sql, new { ColumnName = columnName }).ConfigureAwait(false);
            return sqliteCount > 0;
        }

        var query = databaseType switch
        {
            DatabaseType.SqlServer => "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName",
            DatabaseType.PostgreSQL => "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @TableName AND column_name = @ColumnName",
            DatabaseType.MySQL => "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @TableName AND column_name = @ColumnName",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };

        var count = await connection.QuerySingleAsync<int>(query, new { TableName = tableName, ColumnName = columnName }).ConfigureAwait(false);
        return count > 0;
    }

    private async Task EnsureWikiConfigurationOwnershipColumnsAsync(IDbConnection connection, DatabaseType databaseType)
    {
        var tableName = databaseType == DatabaseType.SqlServer ? "WikiConfigurations" : "wiki_configurations";

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "OwnerUserId" : "owner_user_id").ConfigureAwait(false))
        {
            var sql = databaseType == DatabaseType.SqlServer
                ? "ALTER TABLE [dbo].[WikiConfigurations] ADD [OwnerUserId] NVARCHAR(128) NULL"
                : "ALTER TABLE wiki_configurations ADD COLUMN owner_user_id TEXT";
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "OwnerDisplayName" : "owner_display_name").ConfigureAwait(false))
        {
            var sql = databaseType == DatabaseType.SqlServer
                ? "ALTER TABLE [dbo].[WikiConfigurations] ADD [OwnerDisplayName] NVARCHAR(255) NULL"
                : "ALTER TABLE wiki_configurations ADD COLUMN owner_display_name TEXT";
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "VisibilityScope" : "visibility_scope").ConfigureAwait(false))
        {
            var sql = databaseType == DatabaseType.SqlServer
                ? "ALTER TABLE [dbo].[WikiConfigurations] ADD [VisibilityScope] NVARCHAR(32) NOT NULL CONSTRAINT DF_WikiConfigurations_VisibilityScope DEFAULT('Global')"
                : "ALTER TABLE wiki_configurations ADD COLUMN visibility_scope TEXT NOT NULL DEFAULT 'Global'";
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "CreatedByAdmin" : "created_by_admin").ConfigureAwait(false))
        {
            var sql = databaseType == DatabaseType.SqlServer
                ? "ALTER TABLE [dbo].[WikiConfigurations] ADD [CreatedByAdmin] BIT NOT NULL CONSTRAINT DF_WikiConfigurations_CreatedByAdmin DEFAULT(0)"
                : "ALTER TABLE wiki_configurations ADD COLUMN created_by_admin INTEGER NOT NULL DEFAULT 0";
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }

        if (!await ColumnExistsAsync(connection, tableName, databaseType == DatabaseType.SqlServer ? "RootPath" : "root_path").ConfigureAwait(false))
        {
            var sql = databaseType switch
            {
                DatabaseType.SqlServer => "ALTER TABLE [dbo].[WikiConfigurations] ADD [RootPath] NVARCHAR(2000) NULL",
                DatabaseType.PostgreSQL => "ALTER TABLE wiki_configurations ADD COLUMN root_path TEXT",
                DatabaseType.MySQL => "ALTER TABLE wiki_configurations ADD COLUMN root_path TEXT NULL",
                _ => "ALTER TABLE wiki_configurations ADD COLUMN root_path TEXT"
            };
            await connection.ExecuteAsync(sql).ConfigureAwait(false);
        }
    }

    private async Task EnsureSchemaVersion2PreferredLanguageAsync(IDbConnection connection, DatabaseType databaseType)
    {
        var schemaVersionTable = GetTableName("schema_version", databaseType);
        var currentVersionSql = $"SELECT MAX({(databaseType == DatabaseType.SqlServer ? "[Version]" : "version")}) FROM {schemaVersionTable}";
        var currentVersion = await connection.QuerySingleOrDefaultAsync<int?>(currentVersionSql).ConfigureAwait(false) ?? 0;
        if (currentVersion >= 2)
        {
            return;
        }

        var alterUserSql = databaseType switch
        {
            DatabaseType.SqlServer => "ALTER TABLE [dbo].[Users] ADD [PreferredLanguage] NVARCHAR(16) NULL",
            DatabaseType.PostgreSQL => "ALTER TABLE users ADD COLUMN preferred_language VARCHAR(16)",
            DatabaseType.MySQL => "ALTER TABLE users ADD COLUMN preferred_language VARCHAR(16) NULL",
            DatabaseType.SQLite => "ALTER TABLE users ADD COLUMN preferred_language TEXT NULL",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };
        await connection.ExecuteAsync(alterUserSql).ConfigureAwait(false);

        var insertVersionSql = databaseType switch
        {
            DatabaseType.SqlServer => "INSERT INTO [dbo].[SchemaVersion] ([Version], [Description], [AppliedAt]) VALUES (2, 'Add preferred language to users', GETDATE())",
            DatabaseType.PostgreSQL => "INSERT INTO schema_version (version, description, applied_at) VALUES (2, 'Add preferred language to users', CURRENT_TIMESTAMP)",
            DatabaseType.MySQL => "INSERT INTO schema_version (version, description, applied_at) VALUES (2, 'Add preferred language to users', CURRENT_TIMESTAMP)",
            DatabaseType.SQLite => "INSERT INTO schema_version (version, description, applied_at) VALUES (2, 'Add preferred language to users', datetime('now'))",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };
        await connection.ExecuteAsync(insertVersionSql).ConfigureAwait(false);
    }

    private static async Task EnsureAiProvidersTableAsync(IDbConnection connection, DatabaseType databaseType)
    {
        string sql = databaseType switch
        {
            DatabaseType.SqlServer => @"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AiProviders]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AiProviders] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [ProviderName] NVARCHAR(100) NOT NULL,
        [DisplayName] NVARCHAR(150) NOT NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [IsDefault] BIT NOT NULL DEFAULT 0,
        [EndpointUrl] NVARCHAR(500),
        [ApiKey] NVARCHAR(1024),
        [ModelName] NVARCHAR(200),
        [ApiVersion] NVARCHAR(100),
        [OrganizationId] NVARCHAR(200),
        [ConfigurationJson] NVARCHAR(MAX),
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [LastModifiedAt] DATETIME2
    );
    CREATE INDEX IX_AiProviders_ProviderName ON [dbo].[AiProviders]([ProviderName]);
    CREATE INDEX IX_AiProviders_IsEnabled ON [dbo].[AiProviders]([IsEnabled]);
    CREATE INDEX IX_AiProviders_IsDefault ON [dbo].[AiProviders]([IsDefault]);
END",
            DatabaseType.SQLite => @"
CREATE TABLE IF NOT EXISTS ai_providers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_name TEXT NOT NULL,
    display_name TEXT NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    is_default INTEGER NOT NULL DEFAULT 0,
    endpoint_url TEXT,
    api_key TEXT,
    model_name TEXT,
    api_version TEXT,
    organization_id TEXT,
    configuration_json TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_modified_at TEXT
);
CREATE INDEX IF NOT EXISTS ix_ai_providers_provider_name ON ai_providers(provider_name);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_enabled ON ai_providers(is_enabled);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_default ON ai_providers(is_default);",
            DatabaseType.PostgreSQL => @"
CREATE TABLE IF NOT EXISTS ai_providers (
    id SERIAL PRIMARY KEY,
    provider_name VARCHAR(100) NOT NULL,
    display_name VARCHAR(150) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    endpoint_url VARCHAR(500),
    api_key VARCHAR(1024),
    model_name VARCHAR(200),
    api_version VARCHAR(100),
    organization_id VARCHAR(200),
    configuration_json TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_modified_at TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_ai_providers_provider_name ON ai_providers(provider_name);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_enabled ON ai_providers(is_enabled);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_default ON ai_providers(is_default);",
            DatabaseType.MySQL => @"
CREATE TABLE IF NOT EXISTS ai_providers (
    id INT AUTO_INCREMENT PRIMARY KEY,
    provider_name VARCHAR(100) NOT NULL,
    display_name VARCHAR(150) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    endpoint_url VARCHAR(500),
    api_key VARCHAR(1024),
    model_name VARCHAR(200),
    api_version VARCHAR(100),
    organization_id VARCHAR(200),
    configuration_json TEXT,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_modified_at DATETIME,
    INDEX ix_ai_providers_provider_name (provider_name),
    INDEX ix_ai_providers_is_enabled (is_enabled),
    INDEX ix_ai_providers_is_default (is_default)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private static async Task EnsureAccessPoliciesTablesAsync(IDbConnection connection, DatabaseType databaseType)
    {
        var sql = databaseType switch
        {
            DatabaseType.SqlServer => @"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AccessPolicies]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AccessPolicies] (
        [id] NVARCHAR(64) NOT NULL PRIMARY KEY,
        [identity_type] INT NOT NULL,
        [identity_id] NVARCHAR(128) NOT NULL,
        [identity_display_name] NVARCHAR(255) NOT NULL,
        [is_admin] BIT NOT NULL DEFAULT 0,
        [system_manage_wikis] BIT NOT NULL DEFAULT 0,
        [system_manage_users_and_groups] BIT NOT NULL DEFAULT 0,
        [system_manage_permissions] BIT NOT NULL DEFAULT 0,
        [created_at] DATETIME2 NOT NULL,
        [last_modified_at] DATETIME2 NOT NULL,
        [is_active] BIT NOT NULL DEFAULT 1
    );
    CREATE INDEX IX_AccessPolicies_Identity ON [dbo].[AccessPolicies]([identity_type],[identity_id]);
    CREATE INDEX IX_AccessPolicies_IsActive ON [dbo].[AccessPolicies]([is_active]);
END;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AccessPolicyWikis]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AccessPolicyWikis] (
        [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [policy_id] NVARCHAR(64) NOT NULL,
        [wiki_id] NVARCHAR(128) NOT NULL,
        [start_points] NVARCHAR(4000) NULL,
        [can_view] BIT NOT NULL DEFAULT 0,
        [can_comment] BIT NOT NULL DEFAULT 0,
        [can_export_word] BIT NOT NULL DEFAULT 0,
        [can_export_pdf] BIT NOT NULL DEFAULT 0,
        [can_use_letterhead] BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_AccessPolicyWikis_AccessPolicies
            FOREIGN KEY ([policy_id]) REFERENCES [dbo].[AccessPolicies]([id]) ON DELETE CASCADE
    );
    CREATE INDEX IX_AccessPolicyWikis_PolicyId ON [dbo].[AccessPolicyWikis]([policy_id]);
    CREATE INDEX IX_AccessPolicyWikis_WikiId ON [dbo].[AccessPolicyWikis]([wiki_id]);
END;",
            DatabaseType.SQLite => @"
CREATE TABLE IF NOT EXISTS access_policies (
    id TEXT PRIMARY KEY,
    identity_type INTEGER NOT NULL,
    identity_id TEXT NOT NULL,
    identity_display_name TEXT NOT NULL,
    is_admin INTEGER NOT NULL DEFAULT 0,
    system_manage_wikis INTEGER NOT NULL DEFAULT 0,
    system_manage_users_and_groups INTEGER NOT NULL DEFAULT 0,
    system_manage_permissions INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    last_modified_at TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_access_policies_identity ON access_policies(identity_type, identity_id);
CREATE INDEX IF NOT EXISTS ix_access_policies_is_active ON access_policies(is_active);

CREATE TABLE IF NOT EXISTS access_policy_wikis (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    policy_id TEXT NOT NULL,
    wiki_id TEXT NOT NULL,
    start_points TEXT,
    can_view INTEGER NOT NULL DEFAULT 0,
    can_comment INTEGER NOT NULL DEFAULT 0,
    can_export_word INTEGER NOT NULL DEFAULT 0,
    can_export_pdf INTEGER NOT NULL DEFAULT 0,
    can_use_letterhead INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(policy_id) REFERENCES access_policies(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_policy_id ON access_policy_wikis(policy_id);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_wiki_id ON access_policy_wikis(wiki_id);",
            DatabaseType.PostgreSQL => @"
CREATE TABLE IF NOT EXISTS access_policies (
    id VARCHAR(64) PRIMARY KEY,
    identity_type INTEGER NOT NULL,
    identity_id VARCHAR(128) NOT NULL,
    identity_display_name VARCHAR(255) NOT NULL,
    is_admin BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_wikis BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_users_and_groups BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL,
    last_modified_at TIMESTAMP NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE INDEX IF NOT EXISTS ix_access_policies_identity ON access_policies(identity_type, identity_id);
CREATE INDEX IF NOT EXISTS ix_access_policies_is_active ON access_policies(is_active);

CREATE TABLE IF NOT EXISTS access_policy_wikis (
    id SERIAL PRIMARY KEY,
    policy_id VARCHAR(64) NOT NULL REFERENCES access_policies(id) ON DELETE CASCADE,
    wiki_id VARCHAR(128) NOT NULL,
    start_points TEXT,
    can_view BOOLEAN NOT NULL DEFAULT FALSE,
    can_comment BOOLEAN NOT NULL DEFAULT FALSE,
    can_export_word BOOLEAN NOT NULL DEFAULT FALSE,
    can_export_pdf BOOLEAN NOT NULL DEFAULT FALSE,
    can_use_letterhead BOOLEAN NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_policy_id ON access_policy_wikis(policy_id);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_wiki_id ON access_policy_wikis(wiki_id);",
            DatabaseType.MySQL => @"
CREATE TABLE IF NOT EXISTS access_policies (
    id VARCHAR(64) NOT NULL PRIMARY KEY,
    identity_type INT NOT NULL,
    identity_id VARCHAR(128) NOT NULL,
    identity_display_name VARCHAR(255) NOT NULL,
    is_admin BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_wikis BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_users_and_groups BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    created_at DATETIME NOT NULL,
    last_modified_at DATETIME NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE INDEX ix_access_policies_identity ON access_policies(identity_type, identity_id);
CREATE INDEX ix_access_policies_is_active ON access_policies(is_active);

CREATE TABLE IF NOT EXISTS access_policy_wikis (
    id INT AUTO_INCREMENT PRIMARY KEY,
    policy_id VARCHAR(64) NOT NULL,
    wiki_id VARCHAR(128) NOT NULL,
    start_points TEXT NULL,
    can_view BOOLEAN NOT NULL DEFAULT FALSE,
    can_comment BOOLEAN NOT NULL DEFAULT FALSE,
    can_export_word BOOLEAN NOT NULL DEFAULT FALSE,
    can_export_pdf BOOLEAN NOT NULL DEFAULT FALSE,
    can_use_letterhead BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_access_policy_wikis_policy
        FOREIGN KEY (policy_id) REFERENCES access_policies(id) ON DELETE CASCADE
);
CREATE INDEX ix_access_policy_wikis_policy_id ON access_policy_wikis(policy_id);
CREATE INDEX ix_access_policy_wikis_wiki_id ON access_policy_wikis(wiki_id);",
            _ => throw new NotSupportedException($"Database type {databaseType} not supported")
        };

        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private string GetSchemaScript(DatabaseType databaseType)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePath = databaseType switch
        {
            DatabaseType.SqlServer => "ExportAzureWiki.Data.Schema.Scripts.SqlServer.001_InitialSchema.sql",
            DatabaseType.PostgreSQL => "ExportAzureWiki.Data.Schema.Scripts.PostgreSQL.001_InitialSchema.sql",
            DatabaseType.MySQL => "ExportAzureWiki.Data.Schema.Scripts.MySQL.001_InitialSchema.sql",
            DatabaseType.SQLite => "ExportAzureWiki.Data.Schema.Scripts.SQLite.001_InitialSchema.sql",
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };

        // Try to read from embedded resource first
        using var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // Fallback to reading from file system
        var basePath = Path.GetDirectoryName(assembly.Location) ?? string.Empty;
        var scriptPath = databaseType switch
        {
            DatabaseType.SqlServer => Path.Combine(basePath, "Data", "Schema", "Scripts", "SqlServer", "001_InitialSchema.sql"),
            DatabaseType.PostgreSQL => Path.Combine(basePath, "Data", "Schema", "Scripts", "PostgreSQL", "001_InitialSchema.sql"),
            DatabaseType.MySQL => Path.Combine(basePath, "Data", "Schema", "Scripts", "MySQL", "001_InitialSchema.sql"),
            DatabaseType.SQLite => Path.Combine(basePath, "Data", "Schema", "Scripts", "SQLite", "001_InitialSchema.sql"),
            _ => throw new NotSupportedException($"Database type {databaseType} is not supported")
        };

        if (File.Exists(scriptPath))
        {
            return File.ReadAllText(scriptPath);
        }

        throw new FileNotFoundException($"Schema script not found: {scriptPath}");
    }

    private List<string> SplitScriptIntoBatches(string script, DatabaseType databaseType)
    {
        if (databaseType == DatabaseType.SqlServer)
        {
            // Split by GO for SQL Server
            return script
                .Split(new[] { "\nGO\n", "\nGO\r\n", "\r\nGO\r\n", "\r\nGO\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        else
        {
            // For other databases, execute as single script
            return new List<string> { script };
        }
    }

    private string GetTableName(string tableName, DatabaseType databaseType)
    {
        if (databaseType != DatabaseType.SqlServer)
        {
            return tableName;
        }

        var sqlServerTableName = tableName.ToLowerInvariant() switch
        {
            "application_settings" => "ApplicationSettings",
            "audit_log" => "AuditLog",
            "authentication_configuration" => "AuthenticationConfiguration",
            "access_policies" => "AccessPolicies",
            "access_policy_wikis" => "AccessPolicyWikis",
            "identity_groups" => "IdentityGroups",
            "oauth_providers" => "OAuthProviders",
            "ai_providers" => "AiProviders",
            "schema_version" => "SchemaVersion",
            "sessions" => "Sessions",
            "user_identity_groups" => "UserIdentityGroups",
            "users" => "Users",
            "wiki_configurations" => "WikiConfigurations",
            _ => tableName
        };

        return $"[dbo].[{sqlServerTableName}]";
    }

    /// <summary>
    /// Seeds the database with pre-configured OAuth providers (inactive, awaiting credentials)
    /// </summary>
    public async Task<bool> SeedOAuthProvidersAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
            var databaseType = _connectionFactory.GetDatabaseType();
            var tableName = GetTableName("oauth_providers", databaseType);

            // Check if providers already exist
            var countSql = $"SELECT COUNT(*) FROM {tableName}";
            var existingCount = await connection.QuerySingleAsync<int>(countSql).ConfigureAwait(false);

            if (existingCount > 0)
            {
                // Providers already seeded
                return true;
            }

            // Pre-configured providers. They are seeded disabled and awaiting
            // credentials, so ClientId is empty (the column is NOT NULL across
            // all engines; an empty string satisfies it without a migration).
            var providers = new[]
            {
                new
                {
                    ProviderName = "AzureAD",
                    DisplayName = "Azure Active Directory",
                    ClientId = string.Empty,
                    IsEnabled = false,
                    CreatedAt = DateTime.Now
                },
                new
                {
                    ProviderName = "GitHub",
                    DisplayName = "GitHub",
                    ClientId = string.Empty,
                    IsEnabled = false,
                    CreatedAt = DateTime.Now
                },
                new
                {
                    ProviderName = "Google",
                    DisplayName = "Google",
                    ClientId = string.Empty,
                    IsEnabled = false,
                    CreatedAt = DateTime.Now
                },
                new
                {
                    ProviderName = "Microsoft",
                    DisplayName = "Microsoft",
                    ClientId = string.Empty,
                    IsEnabled = false,
                    CreatedAt = DateTime.Now
                }
            };

            // Build INSERT statement based on database type
            foreach (var provider in providers)
            {
                string insertSql;

                if (databaseType == DatabaseType.SqlServer)
                {
                    insertSql = $@"INSERT INTO {tableName}
                        (ProviderName, DisplayName, ClientId, IsEnabled, CreatedAt)
                        VALUES (@ProviderName, @DisplayName, @ClientId, @IsEnabled, @CreatedAt)";
                }
                else
                {
                    var columns = "provider_name, display_name, client_id, is_enabled, created_at";
                    insertSql = $@"INSERT INTO {tableName} ({columns})
                        VALUES (@ProviderName, @DisplayName, @ClientId, @IsEnabled, @CreatedAt)";
                }

                await connection.ExecuteAsync(insertSql, provider).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error seeding OAuth providers: {ex.Message}");
            return false;
        }
    }
}
