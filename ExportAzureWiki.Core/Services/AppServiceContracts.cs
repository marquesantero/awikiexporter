using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Core.Services;

public interface IAuthenticationService
{
    bool IsAuthenticated { get; }
    AuthenticatedUser? CurrentUser { get; }
    Task<AuthenticationOutcome> AuthenticateLocalAsync(string usernameOrEmail, string password);
    Task<AuthenticationOutcome> AuthenticateAzureAsync();
    Task SaveCurrentUserPreferredLanguageAsync(string languageCode);
    void SignOut();
}

public interface IWikiCatalogService
{
    Task<IReadOnlyList<WikiConfiguration>> LoadAsync();
    Task SaveAsync(IReadOnlyList<WikiConfiguration> items);
    Task<bool> DeleteByIdAsync(string id);
}

public interface IWikiPageBrowserService
{
    Task<IReadOnlyList<WikiPage>> GetPagesAsync(WikiConfiguration configuration);
    Task<WikiPageContent?> GetPageContentAsync(WikiConfiguration configuration, string pagePath);
}

public interface IWikiPageRenderService
{
    Task<IReadOnlyList<RenderedWikiPage>> RenderWikiPagesAsync(
        WikiConfiguration configuration,
        IReadOnlyList<string> pagePaths,
        bool forceRefreshCache,
        bool offlineMode);

    Task<RenderedWikiPage?> RenderLocalMarkdownAsync(string markdownFilePath);
}

public interface IAdminCatalogService
{
    Task<IReadOnlyList<UserRecord>> LoadUsersAsync();
    Task<IReadOnlyList<IdentityGroup>> LoadGroupsAsync();
    Task<IReadOnlyList<OAuthProvider>> LoadOAuthProvidersAsync();
    Task<IReadOnlyList<AiProvider>> LoadAiProvidersAsync();
    Task<AuthenticationConfiguration?> LoadAuthConfigurationAsync();
    Task<IReadOnlyList<AccessPolicy>> LoadAccessPoliciesAsync();
    Task<int> SaveUserAsync(UserRecord user, string? plainPassword = null);
    Task<bool> DeleteUserAsync(int id);
    Task<int> SaveGroupAsync(IdentityGroup group);
    Task<bool> DeleteGroupAsync(int id);
    Task<IDictionary<int, int>> LoadGroupMemberCountsAsync();
    Task<IReadOnlyList<WikiConfiguration>> LoadWikisAsync();
    Task<AccessPolicy> GetOrCreateAccessPolicyAsync(AccessPolicyIdentityType identityType, string identityId, string identityDisplayName);
    Task SaveAccessPolicyAsync(AccessPolicy policy);
    Task<int> SaveOAuthProviderAsync(OAuthProvider provider);
    Task<bool> DeleteOAuthProviderAsync(int id);
    Task<int> SaveAiProviderAsync(AiProvider provider);
    Task<bool> DeleteAiProviderAsync(int id);
    Task<bool> SaveAuthenticationConfigurationAsync(AuthenticationConfiguration configuration);
    Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm);
    Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm, int? providerId);
}

public interface IAiTextGenerationService
{
    Task<string> GenerateSummaryAsync(string sourceContent);
    Task<string> GenerateIndexAsync(string sourceContent);
    Task<string> GenerateQuizAsync(string sourceContent, int directQuestions, int multipleChoiceQuestions);
    Task<string> AnswerQuestionAsync(string question, string sourceContent);
}

/// <summary>Result of probing an AI provider (connection test).</summary>
public sealed record AiProviderProbeResult(bool Success, string Message, IReadOnlyList<string> Models);

/// <summary>
/// Validates an AI provider configuration and discovers its available models
/// live (no hardcoded model lists), operating on the in-edit provider rather
/// than the configured default.
/// </summary>
public interface IAiProviderProbeService
{
    Task<IReadOnlyList<string>> ListModelsAsync(AiProvider provider, CancellationToken cancellationToken = default);
    Task<AiProviderProbeResult> TestAsync(AiProvider provider, CancellationToken cancellationToken = default);
}

public interface IDocumentExportService
{
    Task ExportToWordAsync(string html, string filePath, bool applyWordFineTune = false, bool refreshImageCache = false);
    Task ExportToPdfAsync(string html, string filePath);
}

public sealed class ExportHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string Format { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int SourcePages { get; set; }
    public bool Success { get; set; }
    public int DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DetailsJson { get; set; }
}

public interface IExportHistoryService
{
    Task RecordAsync(ExportHistoryEntry entry);
}

public interface IAppServiceSet
{
    IAuthenticationService Authentication { get; }
    IWikiCatalogService WikiCatalog { get; }
    IWikiPageBrowserService WikiPageBrowser { get; }
    IWikiPageRenderService WikiPageRenderer { get; }
    IAdminCatalogService AdminCatalog { get; }
    IAiTextGenerationService AiTextGeneration { get; }
    IAiProviderProbeService AiProviderProbe { get; }
    IDocumentExportService DocumentExport { get; }
    IExportHistoryService ExportHistory { get; }
}
