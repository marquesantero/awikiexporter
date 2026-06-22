-- ExportAzureWiki Database Schema - SQL Server
-- Version: 001 - Baseline (No Legacy)
-- Date: 2026-02-28

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [Username] NVARCHAR(255) NOT NULL UNIQUE,
        [Email] NVARCHAR(255) NULL,
        [PasswordHash] NVARCHAR(512) NOT NULL,
        [PasswordSalt] NVARCHAR(512) NOT NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [LastLoginAt] DATETIME2 NULL,
        [LastModifiedAt] DATETIME2 NULL,
        [AuthenticationMethod] INT NULL,
        [ExternalId] NVARCHAR(255) NULL,
        [DisplayName] NVARCHAR(255) NULL,
        [PreferredLanguage] NVARCHAR(16) NULL
    );
    CREATE INDEX IX_Users_Username ON [dbo].[Users]([Username]);
    CREATE INDEX IX_Users_Email ON [dbo].[Users]([Email]);
    CREATE INDEX IX_Users_ExternalId ON [dbo].[Users]([ExternalId]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[OAuthProviders]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[OAuthProviders] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [ProviderName] NVARCHAR(100) NOT NULL,
        [DisplayName] NVARCHAR(150) NOT NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [ClientId] NVARCHAR(512) NOT NULL,
        [ClientSecret] NVARCHAR(1024) NULL,
        [TenantId] NVARCHAR(512) NULL,
        [RedirectUri] NVARCHAR(500) NULL,
        [Scopes] NVARCHAR(MAX) NULL,
        [ConfigurationJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [LastModifiedAt] DATETIME2 NULL
    );
    CREATE INDEX IX_OAuthProviders_ProviderName ON [dbo].[OAuthProviders]([ProviderName]);
    CREATE INDEX IX_OAuthProviders_IsEnabled ON [dbo].[OAuthProviders]([IsEnabled]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AiProviders]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AiProviders] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [ProviderName] NVARCHAR(100) NOT NULL,
        [DisplayName] NVARCHAR(150) NOT NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [IsDefault] BIT NOT NULL DEFAULT 0,
        [EndpointUrl] NVARCHAR(500) NULL,
        [ApiKey] NVARCHAR(MAX) NULL,
        [ModelName] NVARCHAR(200) NULL,
        [ApiVersion] NVARCHAR(100) NULL,
        [OrganizationId] NVARCHAR(200) NULL,
        [ConfigurationJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [LastModifiedAt] DATETIME2 NULL
    );
    CREATE INDEX IX_AiProviders_ProviderName ON [dbo].[AiProviders]([ProviderName]);
    CREATE INDEX IX_AiProviders_IsEnabled ON [dbo].[AiProviders]([IsEnabled]);
    CREATE INDEX IX_AiProviders_IsDefault ON [dbo].[AiProviders]([IsDefault]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuthenticationConfiguration]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AuthenticationConfiguration] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [PrimaryMethod] INT NOT NULL DEFAULT 0,
        [AllowWindowsAuth] BIT NOT NULL DEFAULT 0,
        [AllowAzureAD] BIT NOT NULL DEFAULT 0,
        [AllowLocalAuth] BIT NOT NULL DEFAULT 1,
        [RequireAuthentication] BIT NOT NULL DEFAULT 1,
        [SyncAzureADGroups] BIT NOT NULL DEFAULT 0,
        [SyncWindowsGroups] BIT NOT NULL DEFAULT 0,
        [AzureADTenantId] NVARCHAR(512) NULL,
        [AutoCreateUsers] BIT NOT NULL DEFAULT 1,
        [DefaultRole] NVARCHAR(100) NOT NULL DEFAULT 'User',
        [UseLocalPermissions] BIT NOT NULL DEFAULT 1,
        [UseAzureADPermissions] BIT NOT NULL DEFAULT 0,
        [UseWindowsPermissions] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME2 NULL
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM [dbo].[AuthenticationConfiguration])
BEGIN
    INSERT INTO [dbo].[AuthenticationConfiguration] ([PrimaryMethod], [AllowLocalAuth], [UseLocalPermissions])
    VALUES (0, 1, 1);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WikiConfigurations]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[WikiConfigurations] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [Name] NVARCHAR(255) NOT NULL,
        [Organization] NVARCHAR(255) NOT NULL,
        [Project] NVARCHAR(255) NOT NULL,
        [WikiIdentifier] NVARCHAR(255) NOT NULL,
        [PersonalAccessToken] NVARCHAR(MAX) NOT NULL,
        [Platform] INT NOT NULL DEFAULT 0,
        [AuthType] INT NOT NULL DEFAULT 0,
        [AuthenticationDataJson] NVARCHAR(MAX) NULL,
        [PlatformSpecificDataJson] NVARCHAR(MAX) NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [IconColor] NVARCHAR(32) NULL,
        [IsDefault] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [LastUsedAt] DATETIME2 NULL,
        [LastModifiedAt] DATETIME2 NULL,
        [OwnerUserId] NVARCHAR(128) NULL,
        [OwnerDisplayName] NVARCHAR(255) NULL,
        [VisibilityScope] NVARCHAR(32) NOT NULL DEFAULT 'Global',
        [CreatedByAdmin] BIT NOT NULL DEFAULT 0,
        [RootPath] NVARCHAR(MAX) NULL
        -- No uniqueness on (Organization, Project, WikiIdentifier): a project may
        -- have several configurations (e.g. one Repo-mode and one Wiki-mode, or
        -- different docs scopes). Identity is the integer primary key.
    );
    CREATE INDEX IX_WikiConfigurations_IsDefault ON [dbo].[WikiConfigurations]([IsDefault]);
    CREATE INDEX IX_WikiConfigurations_Owner ON [dbo].[WikiConfigurations]([OwnerUserId]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[IdentityGroups]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[IdentityGroups] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [Name] NVARCHAR(255) NOT NULL UNIQUE,
        [Description] NVARCHAR(500) NULL,
        [IsSystem] BIT NOT NULL DEFAULT 0,
        [Source] NVARCHAR(50) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[UserIdentityGroups]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[UserIdentityGroups] (
        [UserId] INT NOT NULL,
        [GroupId] INT NOT NULL,
        PRIMARY KEY ([UserId], [GroupId]),
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
        FOREIGN KEY ([GroupId]) REFERENCES [dbo].[IdentityGroups]([Id]) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ApplicationSettings]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ApplicationSettings] (
        [Key] NVARCHAR(100) PRIMARY KEY,
        [Value] NVARCHAR(MAX) NULL,
        [IsEncrypted] BIT NOT NULL DEFAULT 0,
        [LastModifiedAt] DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END
GO

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
    CREATE INDEX IX_AccessPolicies_Identity ON [dbo].[AccessPolicies]([identity_type], [identity_id]);
    CREATE INDEX IX_AccessPolicies_IsActive ON [dbo].[AccessPolicies]([is_active]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AccessPolicyWikis]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AccessPolicyWikis] (
        [id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [policy_id] NVARCHAR(64) NOT NULL,
        [wiki_id] NVARCHAR(128) NOT NULL,
        [start_points] NVARCHAR(MAX) NULL,
        [can_view] BIT NOT NULL DEFAULT 0,
        [can_comment] BIT NOT NULL DEFAULT 0,
        [can_export_word] BIT NOT NULL DEFAULT 0,
        [can_export_pdf] BIT NOT NULL DEFAULT 0,
        [can_use_letterhead] BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_AccessPolicyWikis_AccessPolicies FOREIGN KEY ([policy_id]) REFERENCES [dbo].[AccessPolicies]([id]) ON DELETE CASCADE
    );
    CREATE INDEX IX_AccessPolicyWikis_PolicyId ON [dbo].[AccessPolicyWikis]([policy_id]);
    CREATE INDEX IX_AccessPolicyWikis_WikiId ON [dbo].[AccessPolicyWikis]([wiki_id]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Sessions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Sessions] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [UserId] INT NOT NULL,
        [SessionToken] NVARCHAR(512) NOT NULL UNIQUE,
        [RefreshToken] NVARCHAR(512) NULL,
        [ExpiresAt] DATETIME2 NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [IpAddress] NVARCHAR(45) NULL,
        [UserAgent] NVARCHAR(500) NULL,
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX IX_Sessions_SessionToken ON [dbo].[Sessions]([SessionToken]);
    CREATE INDEX IX_Sessions_ExpiresAt ON [dbo].[Sessions]([ExpiresAt]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AuditLog]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AuditLog] (
        [Id] INT PRIMARY KEY IDENTITY(1,1),
        [UserId] INT NULL,
        [Action] NVARCHAR(100) NOT NULL,
        [EntityType] NVARCHAR(100) NULL,
        [EntityId] INT NULL,
        [Details] NVARCHAR(MAX) NULL,
        [IpAddress] NVARCHAR(45) NULL,
        [Timestamp] DATETIME2 NOT NULL DEFAULT GETDATE(),
        FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE SET NULL
    );
    CREATE INDEX IX_AuditLog_Timestamp ON [dbo].[AuditLog]([Timestamp]);
    CREATE INDEX IX_AuditLog_UserId ON [dbo].[AuditLog]([UserId]);
END
GO

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SchemaVersion]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SchemaVersion] (
        [Version] INT PRIMARY KEY,
        [AppliedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [Description] NVARCHAR(500)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM [dbo].[SchemaVersion] WHERE [Version] = 2)
BEGIN
    INSERT INTO [dbo].[SchemaVersion] ([Version], [Description])
    VALUES (2, 'Baseline no-legacy schema (preferred language)');
END
GO
