using System.Data;
using Dapper;
using ExportAzureWiki.Data;
using ExportAzureWiki.Models;
using System.Text.Json;

namespace ExportAzureWiki.Services;

/// <summary>
/// Marker that prefixes encrypted token columns. Required to distinguish
/// new encrypted values from legacy plaintext PATs that were written by
/// earlier releases. Any value missing this prefix is treated as legacy
/// plaintext on read and re-encrypted on the next write. See Fase 1.6.
/// </summary>
internal static class StoredSecret
{
    public const string EncryptedPrefix = "enc:";

    public static string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        if (plaintext.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            // Already protected. Should not normally happen, but guarding
            // against a double-encrypt accident is cheap.
            return plaintext;
        }

        return EncryptedPrefix + EncryptionHelper.Encrypt(plaintext);
    }

    public static string Reveal(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        if (!stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            // Legacy plaintext token. Returning as-is keeps the connection
            // working; the next SaveAll() will rewrite it as enc:...
            return stored;
        }

        try
        {
            return EncryptionHelper.Decrypt(stored[EncryptedPrefix.Length..]);
        }
        catch
        {
            // Decryption failure (corrupted blob, key from another user)
            // should NOT silently leak the ciphertext upstream. Return
            // empty so the caller surfaces a clear "reauthenticate" error
            // instead of pushing a broken token to the Azure DevOps API.
            return string.Empty;
        }
    }
}

public sealed class WikiConfigurationStorageService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WikiConfigurationStorageService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public List<WikiConfiguration> LoadAll()
    {
        using var connection = _connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
        var dbType = _connectionFactory.GetDatabaseType();
        var sql = BuildSelectSql(dbType);
        var rows = connection.Query<WikiConfigurationRow>(sql).ToList();
        return rows.Select(MapFromRow).ToList();
    }

    public bool DeleteById(string id)
    {
        if (!int.TryParse(id, out var parsedId))
        {
            return false;
        }

        using var connection = _connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
        var dbType = _connectionFactory.GetDatabaseType();
        using var transaction = connection.BeginTransaction();
        try
        {
            var wikiIdValue = parsedId.ToString();
            var cacheScope = WikiCacheScopeHelper.FromWikiId(wikiIdValue);
            var deletePolicyRulesSql = dbType == DatabaseType.SqlServer
                ? "DELETE FROM [dbo].[AccessPolicyWikis] WHERE [wiki_id] = @WikiId"
                : "DELETE FROM access_policy_wikis WHERE wiki_id = @WikiId";

            connection.Execute(deletePolicyRulesSql, new { WikiId = wikiIdValue }, transaction);

            var deleteWikiSql = dbType == DatabaseType.SqlServer
                ? "DELETE FROM [dbo].[WikiConfigurations] WHERE [Id] = @Id"
                : "DELETE FROM wiki_configurations WHERE id = @Id";

            var deleted = connection.Execute(deleteWikiSql, new { Id = parsedId }, transaction) > 0;
            if (!deleted)
            {
                transaction.Rollback();
                return false;
            }

            transaction.Commit();
            PurgeWikiCache(cacheScope);
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void PurgeWikiCache(string cacheScope)
    {
        var cacheBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExportAzureWiki",
            "Cache");

        var targets = new[]
        {
            Path.Combine(cacheBasePath, "WikiPages", cacheScope),
            Path.Combine(cacheBasePath, "WikiImages", cacheScope)
        };

        foreach (var target in targets)
        {
            if (!Directory.Exists(target))
            {
                continue;
            }

            try
            {
                Directory.Delete(target, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only; deletion of wiki remains successful.
            }
        }
    }

    public void SaveAll(IReadOnlyCollection<WikiConfiguration> configurations)
    {
        using var connection = _connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
        using var transaction = connection.BeginTransaction();
        var dbType = _connectionFactory.GetDatabaseType();

        try
        {
            foreach (var configuration in configurations)
            {
                var row = MapToRow(configuration);
                int id;

                // Identity is the integer primary key: an existing row (numeric
                // id) is updated; anything else (a freshly created config still
                // carrying its GUID placeholder id) is inserted as a new row. We
                // deliberately do NOT collapse rows by (organization, project,
                // wiki_identifier) so a project can hold several configurations
                // (e.g. one Repo-mode and one Wiki-mode).
                if (int.TryParse(configuration.Id, out var parsedId) &&
                    ExistsById(connection, transaction, dbType, parsedId))
                {
                    id = parsedId;
                    UpdateById(connection, transaction, dbType, id, row);
                }
                else
                {
                    id = InsertAndReturnId(connection, transaction, dbType, row);
                }

                configuration.Id = id.ToString();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string BuildSelectSql(DatabaseType dbType)
    {
        if (dbType == DatabaseType.SqlServer)
        {
            return """
                   SELECT
                       [Id] AS id,
                       [Name] AS name,
                       [Organization] AS organization,
                       [Project] AS project,
                       [WikiIdentifier] AS wiki_identifier,
                       [PersonalAccessToken] AS personal_access_token,
                       [Platform] AS platform,
                       [AuthType] AS auth_type,
                       [AuthenticationDataJson] AS authentication_data_json,
                       [PlatformSpecificDataJson] AS platform_specific_data_json,
                       [IsActive] AS is_active,
                       [IconColor] AS icon_color,
                       [IsDefault] AS is_default,
                       [OwnerUserId] AS owner_user_id,
                       [OwnerDisplayName] AS owner_display_name,
                       [VisibilityScope] AS visibility_scope,
                       [RootPath] AS root_path,
                       [CreatedByAdmin] AS created_by_admin,
                       [CreatedAt] AS created_at,
                       [LastUsedAt] AS last_used_at,
                       [LastModifiedAt] AS last_modified_at
                   FROM [dbo].[WikiConfigurations]
                   ORDER BY [Name]
                   """;
        }

        return """
               SELECT
                   id,
                   name,
                   organization,
                   project,
                   wiki_identifier,
                   personal_access_token,
                   platform,
                   auth_type,
                   authentication_data_json,
                   platform_specific_data_json,
                   is_active,
                   icon_color,
                   is_default,
                   owner_user_id,
                   owner_display_name,
                   visibility_scope,
                   root_path,
                   created_by_admin,
                   created_at,
                   last_used_at,
                   last_modified_at
               FROM wiki_configurations
               ORDER BY name
               """;
    }

    private static bool ExistsById(IDbConnection connection, IDbTransaction transaction, DatabaseType dbType, int id)
    {
        var sql = dbType == DatabaseType.SqlServer
            ? "SELECT COUNT(1) FROM [dbo].[WikiConfigurations] WHERE [Id] = @Id"
            : "SELECT COUNT(1) FROM wiki_configurations WHERE id = @Id";

        return connection.QuerySingle<int>(sql, new { Id = id }, transaction) > 0;
    }

    private static void UpdateById(IDbConnection connection, IDbTransaction transaction, DatabaseType dbType, int id, WikiConfigurationRow row)
    {
        if (row.is_default)
        {
            var clearSql = dbType == DatabaseType.SqlServer
                ? "UPDATE [dbo].[WikiConfigurations] SET [IsDefault] = 0"
                : "UPDATE wiki_configurations SET is_default = 0";
            connection.Execute(clearSql, transaction: transaction);
        }

        var sql = dbType == DatabaseType.SqlServer
            ? """
              UPDATE [dbo].[WikiConfigurations]
              SET [Name] = @name,
                  [Organization] = @organization,
                  [Project] = @project,
                  [WikiIdentifier] = @wiki_identifier,
                  [PersonalAccessToken] = @personal_access_token,
                  [Platform] = @platform,
                  [AuthType] = @auth_type,
                  [AuthenticationDataJson] = @authentication_data_json,
                  [PlatformSpecificDataJson] = @platform_specific_data_json,
                  [IsActive] = @is_active,
                  [IconColor] = @icon_color,
                  [IsDefault] = @is_default,
                  [OwnerUserId] = @owner_user_id,
                  [OwnerDisplayName] = @owner_display_name,
                  [VisibilityScope] = @visibility_scope,
                  [RootPath] = @root_path,
                  [CreatedByAdmin] = @created_by_admin,
                  [LastUsedAt] = @last_used_at,
                  [LastModifiedAt] = GETDATE()
              WHERE [Id] = @id
              """
            : """
              UPDATE wiki_configurations
              SET name = @name,
                  organization = @organization,
                  project = @project,
                  wiki_identifier = @wiki_identifier,
                  personal_access_token = @personal_access_token,
                  platform = @platform,
                  auth_type = @auth_type,
                  authentication_data_json = @authentication_data_json,
                  platform_specific_data_json = @platform_specific_data_json,
                  is_active = @is_active,
                  icon_color = @icon_color,
                  is_default = @is_default,
                  owner_user_id = @owner_user_id,
                  owner_display_name = @owner_display_name,
                  visibility_scope = @visibility_scope,
                  root_path = @root_path,
                  created_by_admin = @created_by_admin,
                  last_used_at = @last_used_at,
                  last_modified_at = CURRENT_TIMESTAMP
              WHERE id = @id
              """;

        connection.Execute(sql, new
        {
            id,
            row.name,
            row.organization,
            row.project,
            row.wiki_identifier,
            row.personal_access_token,
            row.platform,
            row.auth_type,
            row.authentication_data_json,
            row.platform_specific_data_json,
            row.is_active,
            row.icon_color,
            row.is_default,
            row.owner_user_id,
            row.owner_display_name,
            row.visibility_scope,
            row.created_by_admin,
            row.root_path,
            row.last_used_at
        }, transaction);
    }

    private static int InsertAndReturnId(IDbConnection connection, IDbTransaction transaction, DatabaseType dbType, WikiConfigurationRow row)
    {
        if (row.is_default)
        {
            var clearSql = dbType == DatabaseType.SqlServer
                ? "UPDATE [dbo].[WikiConfigurations] SET [IsDefault] = 0"
                : "UPDATE wiki_configurations SET is_default = 0";
            connection.Execute(clearSql, transaction: transaction);
        }

        if (dbType == DatabaseType.SqlServer)
        {
            var sql = """
                      INSERT INTO [dbo].[WikiConfigurations]
                          ([Name], [Organization], [Project], [WikiIdentifier], [PersonalAccessToken], [Platform], [AuthType], [AuthenticationDataJson], [PlatformSpecificDataJson], [IsActive], [IconColor], [IsDefault], [OwnerUserId], [OwnerDisplayName], [VisibilityScope], [RootPath], [CreatedByAdmin], [CreatedAt], [LastUsedAt], [LastModifiedAt])
                      VALUES
                          (@name, @organization, @project, @wiki_identifier, @personal_access_token, @platform, @auth_type, @authentication_data_json, @platform_specific_data_json, @is_active, @icon_color, @is_default, @owner_user_id, @owner_display_name, @visibility_scope, @root_path, @created_by_admin, GETDATE(), @last_used_at, GETDATE());
                      SELECT CAST(SCOPE_IDENTITY() AS int);
                      """;
            return connection.QuerySingle<int>(sql, row, transaction);
        }

        if (dbType == DatabaseType.PostgreSQL)
        {
            var sql = """
                      INSERT INTO wiki_configurations
                          (name, organization, project, wiki_identifier, personal_access_token, platform, auth_type, authentication_data_json, platform_specific_data_json, is_active, icon_color, is_default, owner_user_id, owner_display_name, visibility_scope, root_path, created_by_admin, created_at, last_used_at, last_modified_at)
                      VALUES
                          (@name, @organization, @project, @wiki_identifier, @personal_access_token, @platform, @auth_type, @authentication_data_json, @platform_specific_data_json, @is_active, @icon_color, @is_default, @owner_user_id, @owner_display_name, @visibility_scope, @root_path, @created_by_admin, CURRENT_TIMESTAMP, @last_used_at, CURRENT_TIMESTAMP)
                      RETURNING id;
                      """;
            return connection.QuerySingle<int>(sql, row, transaction);
        }

        if (dbType == DatabaseType.MySQL)
        {
            var sql = """
                      INSERT INTO wiki_configurations
                          (name, organization, project, wiki_identifier, personal_access_token, platform, auth_type, authentication_data_json, platform_specific_data_json, is_active, icon_color, is_default, owner_user_id, owner_display_name, visibility_scope, root_path, created_by_admin, created_at, last_used_at, last_modified_at)
                      VALUES
                          (@name, @organization, @project, @wiki_identifier, @personal_access_token, @platform, @auth_type, @authentication_data_json, @platform_specific_data_json, @is_active, @icon_color, @is_default, @owner_user_id, @owner_display_name, @visibility_scope, @root_path, @created_by_admin, NOW(), @last_used_at, NOW());
                      """;
            connection.Execute(sql, row, transaction);
            return connection.QuerySingle<int>("SELECT LAST_INSERT_ID();", transaction: transaction);
        }

        var sqliteSql = """
                        INSERT INTO wiki_configurations
                            (name, organization, project, wiki_identifier, personal_access_token, platform, auth_type, authentication_data_json, platform_specific_data_json, is_active, icon_color, is_default, owner_user_id, owner_display_name, visibility_scope, root_path, created_by_admin, created_at, last_used_at, last_modified_at)
                        VALUES
                            (@name, @organization, @project, @wiki_identifier, @personal_access_token, @platform, @auth_type, @authentication_data_json, @platform_specific_data_json, @is_active, @icon_color, @is_default, @owner_user_id, @owner_display_name, @visibility_scope, @root_path, @created_by_admin, CURRENT_TIMESTAMP, @last_used_at, CURRENT_TIMESTAMP);
                        """;
        connection.Execute(sqliteSql, row, transaction);
        return connection.QuerySingle<int>("SELECT last_insert_rowid();", transaction: transaction);
    }

    private static WikiConfiguration MapFromRow(WikiConfigurationRow row)
    {
        var scope = string.Equals(row.visibility_scope, "Personal", StringComparison.OrdinalIgnoreCase)
            ? WikiVisibilityScope.Personal
            : WikiVisibilityScope.Global;

        var wiki = new WikiConfiguration
        {
            Id = row.id.ToString(),
            Name = row.name ?? string.Empty,
            Platform = (WikiPlatform)row.platform,
            BaseUrl = row.organization ?? string.Empty,
            AuthType = (AuthenticationType)row.auth_type,
            AuthenticationData = RevealAuthenticationData(DeserializeDictionary(row.authentication_data_json)),
            PlatformSpecificData = DeserializeDictionary(row.platform_specific_data_json),
            IsDefault = row.is_default,
            OwnerUserId = row.owner_user_id ?? string.Empty,
            OwnerDisplayName = row.owner_display_name ?? string.Empty,
            VisibilityScope = scope,
            CreatedByAdmin = row.created_by_admin,
            RootPath = row.root_path ?? string.Empty,
            IsActive = row.is_active,
            IconColor = string.IsNullOrWhiteSpace(row.icon_color) ? "#0078D4" : row.icon_color,
            CreatedAt = row.created_at == default ? DateTime.Now : row.created_at,
            LastUsedAt = row.last_used_at ?? row.last_modified_at ?? row.created_at
        };

        return wiki;
    }

    private static WikiConfigurationRow MapToRow(WikiConfiguration wiki)
    {
        var protectedAuthData = ProtectAuthenticationData(wiki.AuthenticationData);

        return new WikiConfigurationRow
        {
            name = wiki.Name ?? string.Empty,
            organization = wiki.BaseUrl ?? string.Empty,
            project = BuildStorageProjectKey(wiki),
            wiki_identifier = BuildStorageWikiKey(wiki),
            personal_access_token = StoredSecret.Protect(wiki.PersonalAccessToken),
            platform = (int)wiki.Platform,
            auth_type = (int)wiki.AuthType,
            authentication_data_json = SerializeDictionary(protectedAuthData),
            platform_specific_data_json = SerializeDictionary(wiki.PlatformSpecificData),
            is_active = wiki.IsActive,
            icon_color = string.IsNullOrWhiteSpace(wiki.IconColor) ? "#0078D4" : wiki.IconColor,
            is_default = wiki.IsDefault,
            owner_user_id = wiki.OwnerUserId,
            owner_display_name = wiki.OwnerDisplayName,
            visibility_scope = wiki.VisibilityScope.ToString(),
            root_path = wiki.RootPath ?? string.Empty,
            created_by_admin = wiki.CreatedByAdmin,
            last_used_at = wiki.LastUsedAt == default ? DateTime.Now : wiki.LastUsedAt
        };
    }

    private static string BuildStorageProjectKey(WikiConfiguration wiki)
    {
        return wiki.Platform switch
        {
            WikiPlatform.AzureDevOps => wiki.ProjectName,
            WikiPlatform.GitHub => wiki.PlatformSpecificData.TryGetValue("Owner", out var owner) ? owner : string.Empty,
            WikiPlatform.GitLab => wiki.PlatformSpecificData.TryGetValue("ProjectId", out var projectId) ? projectId : string.Empty,
            WikiPlatform.Bitbucket => wiki.PlatformSpecificData.TryGetValue("Workspace", out var workspace) ? workspace : string.Empty,
            _ => string.Empty
        };
    }

    private static string BuildStorageWikiKey(WikiConfiguration wiki)
    {
        return wiki.Platform switch
        {
            WikiPlatform.AzureDevOps => !string.IsNullOrWhiteSpace(wiki.WikiName) ? wiki.WikiName : wiki.RepositoryId,
            WikiPlatform.GitHub or WikiPlatform.Bitbucket => wiki.PlatformSpecificData.TryGetValue("Repository", out var repository) ? repository : string.Empty,
            WikiPlatform.GitLab => wiki.PlatformSpecificData.TryGetValue("ProjectId", out var projectId) ? projectId : string.Empty,
            _ => string.Empty
        };
    }

    private static string SerializeDictionary(Dictionary<string, string> values)
        => JsonSerializer.Serialize(values ?? new Dictionary<string, string>());

    private static Dictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }

    private static Dictionary<string, string> ProtectAuthenticationData(Dictionary<string, string> values)
    {
        var copy = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var key in copy.Keys.Where(IsSecretAuthenticationKey).ToList())
        {
            copy[key] = StoredSecret.Protect(copy[key]);
        }

        return copy;
    }

    private static Dictionary<string, string> RevealAuthenticationData(Dictionary<string, string> values)
    {
        foreach (var key in values.Keys.Where(IsSecretAuthenticationKey).ToList())
        {
            values[key] = StoredSecret.Reveal(values[key]);
        }

        return values;
    }

    private static bool IsSecretAuthenticationKey(string key)
        => string.Equals(key, "Token", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "Password", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "ClientSecret", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "ApiKey", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "AppPassword", StringComparison.OrdinalIgnoreCase);

    private sealed class WikiConfigurationRow
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string? organization { get; set; }
        public string? project { get; set; }
        public string? wiki_identifier { get; set; }
        public string? personal_access_token { get; set; }
        public int platform { get; set; }
        public int auth_type { get; set; }
        public string? authentication_data_json { get; set; }
        public string? platform_specific_data_json { get; set; }
        public bool is_active { get; set; } = true;
        public string? icon_color { get; set; }
        public bool is_default { get; set; }
        public string? owner_user_id { get; set; }
        public string? owner_display_name { get; set; }
        public string? visibility_scope { get; set; }
        public string? root_path { get; set; }
        public bool created_by_admin { get; set; }
        public DateTime created_at { get; set; }
        public DateTime? last_used_at { get; set; }
        public DateTime? last_modified_at { get; set; }
    }
}
