using ExportAzureWiki.Core.Models;
using SourceModels = ExportAzureWiki.Models;
using SourceAuth = ExportAzureWiki.Models.Authentication;
using SourceEntities = ExportAzureWiki.Models.Entities;

namespace ExportAzureWiki.Platform.Backend;

internal static class ProviderModelMapper
{
    public static WikiConfiguration ToCore(SourceModels.WikiConfiguration source)
    {
        return new WikiConfiguration
        {
            Id = source.Id,
            Name = source.Name,
            Platform = (WikiPlatform)source.Platform,
            BaseUrl = source.BaseUrl,
            AuthType = (AuthenticationType)source.AuthType,
            AuthenticationData = new Dictionary<string, string>(source.AuthenticationData),
            PlatformSpecificData = new Dictionary<string, string>(source.PlatformSpecificData),
            RootPath = source.RootPath,
            IsDefault = source.IsDefault,
            CreatedAt = source.CreatedAt,
            LastUsedAt = source.LastUsedAt,
            IconColor = source.IconColor,
            IsActive = source.IsActive,
            VisibilityScope = (WikiVisibilityScope)source.VisibilityScope,
            OwnerUserId = source.OwnerUserId,
            OwnerDisplayName = source.OwnerDisplayName,
            CreatedByAdmin = source.CreatedByAdmin
        };
    }

    public static SourceModels.WikiConfiguration ToProvider(WikiConfiguration source)
    {
        return new SourceModels.WikiConfiguration
        {
            Id = source.Id,
            Name = source.Name,
            Platform = (SourceModels.WikiPlatform)source.Platform,
            BaseUrl = source.BaseUrl,
            AuthType = (SourceModels.AuthenticationType)source.AuthType,
            AuthenticationData = new Dictionary<string, string>(source.AuthenticationData),
            PlatformSpecificData = new Dictionary<string, string>(source.PlatformSpecificData),
            RootPath = source.RootPath,
            IsDefault = source.IsDefault,
            CreatedAt = source.CreatedAt,
            LastUsedAt = source.LastUsedAt,
            IconColor = source.IconColor,
            IsActive = source.IsActive,
            VisibilityScope = (SourceModels.WikiVisibilityScope)source.VisibilityScope,
            OwnerUserId = source.OwnerUserId,
            OwnerDisplayName = source.OwnerDisplayName,
            CreatedByAdmin = source.CreatedByAdmin
        };
    }

    public static UserRecord ToCore(SourceEntities.User source)
    {
        return new UserRecord
        {
            Id = source.Id,
            Username = source.Username,
            Email = source.Email,
            DisplayName = source.DisplayName,
            PasswordHash = source.PasswordHash,
            PasswordSalt = source.PasswordSalt,
            IsActive = source.IsActive,
            AuthenticationMethod = source.AuthenticationMethod.HasValue
                ? (Core.Models.AuthenticationMethod?)source.AuthenticationMethod.Value
                : null,
            ExternalId = source.ExternalId,
            PreferredLanguage = source.PreferredLanguage,
            CreatedAt = source.CreatedAt,
            LastLoginAt = source.LastLoginAt,
            LastModifiedAt = source.LastModifiedAt
        };
    }

    public static SourceEntities.User ToProvider(UserRecord source)
    {
        return new SourceEntities.User
        {
            Id = source.Id,
            Username = source.Username,
            Email = source.Email,
            DisplayName = source.DisplayName,
            PasswordHash = source.PasswordHash,
            PasswordSalt = source.PasswordSalt,
            IsActive = source.IsActive,
            AuthenticationMethod = source.AuthenticationMethod.HasValue
                ? (SourceModels.AuthenticationMethod?)source.AuthenticationMethod.Value
                : null,
            ExternalId = source.ExternalId,
            PreferredLanguage = source.PreferredLanguage,
            CreatedAt = source.CreatedAt,
            LastLoginAt = source.LastLoginAt,
            LastModifiedAt = source.LastModifiedAt
        };
    }

    public static SourceEntities.IdentityGroup ToProvider(Core.Models.IdentityGroup source)
    {
        return new SourceEntities.IdentityGroup
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            IsSystem = source.IsSystem,
            Source = source.Source,
            CreatedAt = source.CreatedAt
        };
    }

    public static Core.Models.IdentityGroup ToCore(SourceEntities.IdentityGroup source)
    {
        return new Core.Models.IdentityGroup
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            IsSystem = source.IsSystem,
            Source = source.Source,
            CreatedAt = source.CreatedAt
        };
    }

    public static SourceModels.OAuthProvider ToProvider(Core.Models.OAuthProvider source)
    {
        return new SourceModels.OAuthProvider
        {
            Id = source.Id,
            ProviderName = source.ProviderName,
            DisplayName = source.DisplayName,
            IsEnabled = source.IsEnabled,
            ClientId = source.ClientId,
            ClientSecret = source.ClientSecret,
            TenantId = source.TenantId,
            RedirectUri = source.RedirectUri,
            Scopes = source.Scopes,
            ConfigurationJson = source.ConfigurationJson,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.LastModifiedAt
        };
    }

    public static Core.Models.OAuthProvider ToCore(SourceModels.OAuthProvider source)
    {
        return new Core.Models.OAuthProvider
        {
            Id = source.Id,
            ProviderName = source.ProviderName,
            DisplayName = source.DisplayName,
            IsEnabled = source.IsEnabled,
            ClientId = source.ClientId,
            ClientSecret = source.ClientSecret,
            TenantId = source.TenantId,
            RedirectUri = source.RedirectUri,
            Scopes = source.Scopes,
            ConfigurationJson = source.ConfigurationJson,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.LastModifiedAt
        };
    }

    public static SourceModels.AiProvider ToProvider(Core.Models.AiProvider source)
    {
        return new SourceModels.AiProvider
        {
            Id = source.Id,
            ProviderName = source.ProviderName,
            DisplayName = source.DisplayName,
            IsEnabled = source.IsEnabled,
            IsDefault = source.IsDefault,
            EndpointUrl = source.EndpointUrl,
            ApiKey = source.ApiKey,
            ModelName = source.ModelName,
            ApiVersion = source.ApiVersion,
            OrganizationId = source.OrganizationId,
            ConfigurationJson = source.ConfigurationJson,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.LastModifiedAt
        };
    }

    public static Core.Models.AiProvider ToCore(SourceModels.AiProvider source)
    {
        return new Core.Models.AiProvider
        {
            Id = source.Id,
            ProviderName = source.ProviderName,
            DisplayName = source.DisplayName,
            IsEnabled = source.IsEnabled,
            IsDefault = source.IsDefault,
            EndpointUrl = source.EndpointUrl,
            ApiKey = source.ApiKey,
            ModelName = source.ModelName,
            ApiVersion = source.ApiVersion,
            OrganizationId = source.OrganizationId,
            ConfigurationJson = source.ConfigurationJson,
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.LastModifiedAt
        };
    }

    public static Core.Models.AuthenticationConfiguration ToCore(SourceModels.AuthenticationConfiguration source)
    {
        return new Core.Models.AuthenticationConfiguration
        {
            Id = source.Id,
            PrimaryMethod = (Core.Models.AuthenticationMethod)source.PrimaryMethod,
            AllowWindowsAuth = source.AllowWindowsAuth,
            AllowAzureAD = source.AllowAzureAD,
            AllowLocalAuth = source.AllowLocalAuth,
            RequireAuthentication = source.RequireAuthentication,
            SyncAzureADGroups = source.SyncAzureADGroups,
            SyncWindowsGroups = source.SyncWindowsGroups,
            AzureADTenantId = source.AzureADTenantId,
            AutoCreateUsers = source.AutoCreateUsers,
            DefaultRole = source.DefaultRole,
            UseLocalPermissions = source.UseLocalPermissions,
            UseAzureADPermissions = source.UseAzureADPermissions,
            UseWindowsPermissions = source.UseWindowsPermissions,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    public static SourceModels.AuthenticationConfiguration ToProvider(Core.Models.AuthenticationConfiguration source)
    {
        return new SourceModels.AuthenticationConfiguration
        {
            Id = source.Id,
            PrimaryMethod = (SourceModels.AuthenticationMethod)source.PrimaryMethod,
            AllowWindowsAuth = source.AllowWindowsAuth,
            AllowAzureAD = source.AllowAzureAD,
            AllowLocalAuth = source.AllowLocalAuth,
            RequireAuthentication = source.RequireAuthentication,
            SyncAzureADGroups = source.SyncAzureADGroups,
            SyncWindowsGroups = source.SyncWindowsGroups,
            AzureADTenantId = source.AzureADTenantId,
            AutoCreateUsers = source.AutoCreateUsers,
            DefaultRole = source.DefaultRole,
            UseLocalPermissions = source.UseLocalPermissions,
            UseAzureADPermissions = source.UseAzureADPermissions,
            UseWindowsPermissions = source.UseWindowsPermissions,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    public static Core.Models.AccessPolicy ToCore(SourceAuth.AccessPolicy source)
    {
        return new Core.Models.AccessPolicy
        {
            Id = source.Id,
            IdentityType = (Core.Models.AccessPolicyIdentityType)source.IdentityType,
            IdentityId = source.IdentityId,
            IdentityDisplayName = source.IdentityDisplayName,
            IsAdmin = source.IsAdmin,
            System = new Core.Models.SystemAccessPermissions
            {
                ManageWikis = source.System.ManageWikis,
                ManageUsersAndGroups = source.System.ManageUsersAndGroups,
                ManagePermissions = source.System.ManagePermissions
            },
            Wikis = source.Wikis.Select(rule => new Core.Models.WikiAccessRule
            {
                WikiId = rule.WikiId,
                CanView = rule.CanView,
                StartPoints = rule.StartPoints,
                CanComment = rule.CanComment,
                CanExportWord = rule.CanExportWord,
                CanExportPdf = rule.CanExportPdf,
                CanUseLetterhead = rule.CanUseLetterhead
            }).ToList(),
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.LastModifiedAt,
            IsActive = source.IsActive
        };
    }

    public static SourceAuth.AccessPolicy ToProvider(Core.Models.AccessPolicy source)
    {
        return new SourceAuth.AccessPolicy
        {
            Id = source.Id,
            IdentityType = (SourceAuth.AccessPolicyIdentityType)source.IdentityType,
            IdentityId = source.IdentityId,
            IdentityDisplayName = source.IdentityDisplayName,
            IsAdmin = source.IsAdmin,
            System = new SourceAuth.SystemAccessPermissions
            {
                ManageWikis = source.System.ManageWikis,
                ManageUsersAndGroups = source.System.ManageUsersAndGroups,
                ManagePermissions = source.System.ManagePermissions
            },
            Wikis = source.Wikis.Select(rule => new SourceAuth.WikiAccessRule
            {
                WikiId = rule.WikiId,
                CanView = rule.CanView,
                StartPoints = rule.StartPoints,
                CanComment = rule.CanComment,
                CanExportWord = rule.CanExportWord,
                CanExportPdf = rule.CanExportPdf,
                CanUseLetterhead = rule.CanUseLetterhead
            }).ToList(),
            CreatedAt = source.CreatedAt,
            LastModifiedAt = source.LastModifiedAt,
            IsActive = source.IsActive
        };
    }
}










