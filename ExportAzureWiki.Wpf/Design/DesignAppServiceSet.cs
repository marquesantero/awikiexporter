using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Wpf.Design;

internal sealed class DesignAppServiceSet : IAppServiceSet
{
    public IAuthenticationService Authentication { get; } = new DesignAuthenticationService();
    public IWikiCatalogService WikiCatalog { get; } = new DesignWikiCatalogService();
    public IWikiPageBrowserService WikiPageBrowser { get; } = new DesignWikiPageBrowserService();
    public IWikiPageRenderService WikiPageRenderer { get; } = new DesignWikiPageRenderService();
    public IAdminCatalogService AdminCatalog { get; } = new DesignAdminCatalogService();
    public IAiTextGenerationService AiTextGeneration { get; } = new DesignAiTextGenerationService();
    public IAiProviderProbeService AiProviderProbe { get; } = new DesignAiProviderProbeService();
    public IDocumentExportService DocumentExport { get; } = new DesignDocumentExportService();
    public IExportHistoryService ExportHistory { get; } = new DesignExportHistoryService();

    private sealed class DesignAuthenticationService : IAuthenticationService
    {
        public bool IsAuthenticated => false;
        public AuthenticatedUser? CurrentUser => null;

        public Task<AuthenticationOutcome> AuthenticateLocalAsync(string usernameOrEmail, string password)
            => Task.FromResult(AuthenticationOutcome.Failed("Design mode"));

        public Task<AuthenticationOutcome> AuthenticateAzureAsync()
            => Task.FromResult(AuthenticationOutcome.Failed("Design mode"));

        public Task SaveCurrentUserPreferredLanguageAsync(string languageCode)
            => Task.CompletedTask;

        public void SignOut()
        {
        }
    }

    private sealed class DesignWikiCatalogService : IWikiCatalogService
    {
        public Task<IReadOnlyList<WikiConfiguration>> LoadAsync()
            => Task.FromResult<IReadOnlyList<WikiConfiguration>>([]);

        public Task SaveAsync(IReadOnlyList<WikiConfiguration> items) => Task.CompletedTask;
        public Task<bool> DeleteByIdAsync(string id) => Task.FromResult(false);
    }

    private sealed class DesignWikiPageBrowserService : IWikiPageBrowserService
    {
        public Task<IReadOnlyList<WikiPage>> GetPagesAsync(WikiConfiguration configuration)
            => Task.FromResult<IReadOnlyList<WikiPage>>([]);

        public Task<WikiPageContent?> GetPageContentAsync(WikiConfiguration configuration, string pagePath)
            => Task.FromResult<WikiPageContent?>(null);
    }

    private sealed class DesignWikiPageRenderService : IWikiPageRenderService
    {
        public Task<IReadOnlyList<RenderedWikiPage>> RenderWikiPagesAsync(
            WikiConfiguration configuration,
            IReadOnlyList<string> pagePaths,
            bool forceRefreshCache,
            bool offlineMode)
            => Task.FromResult<IReadOnlyList<RenderedWikiPage>>([]);

        public Task<RenderedWikiPage?> RenderLocalMarkdownAsync(string markdownFilePath)
            => Task.FromResult<RenderedWikiPage?>(null);
    }

    private sealed class DesignAdminCatalogService : IAdminCatalogService
    {
        public Task<IReadOnlyList<UserRecord>> LoadUsersAsync() => Task.FromResult<IReadOnlyList<UserRecord>>([]);
        public Task<IReadOnlyList<IdentityGroup>> LoadGroupsAsync() => Task.FromResult<IReadOnlyList<IdentityGroup>>([]);
        public Task<IReadOnlyList<OAuthProvider>> LoadOAuthProvidersAsync() => Task.FromResult<IReadOnlyList<OAuthProvider>>([]);
        public Task<IReadOnlyList<AiProvider>> LoadAiProvidersAsync() => Task.FromResult<IReadOnlyList<AiProvider>>([]);
        public Task<AuthenticationConfiguration?> LoadAuthConfigurationAsync() => Task.FromResult<AuthenticationConfiguration?>(null);
        public Task<IReadOnlyList<AccessPolicy>> LoadAccessPoliciesAsync() => Task.FromResult<IReadOnlyList<AccessPolicy>>([]);
        public Task<int> SaveUserAsync(UserRecord user, string? plainPassword = null) => Task.FromResult(0);
        public Task<bool> DeleteUserAsync(int id) => Task.FromResult(false);
        public Task<int> SaveGroupAsync(IdentityGroup group) => Task.FromResult(0);
        public Task<bool> DeleteGroupAsync(int id) => Task.FromResult(false);
        public Task<IDictionary<int, int>> LoadGroupMemberCountsAsync() => Task.FromResult<IDictionary<int, int>>(new Dictionary<int, int>());
        public Task<IReadOnlyList<WikiConfiguration>> LoadWikisAsync() => Task.FromResult<IReadOnlyList<WikiConfiguration>>([]);
        public Task<AccessPolicy> GetOrCreateAccessPolicyAsync(AccessPolicyIdentityType identityType, string identityId, string identityDisplayName)
            => Task.FromResult(new AccessPolicy { IdentityType = identityType, IdentityId = identityId, IdentityDisplayName = identityDisplayName });
        public Task SaveAccessPolicyAsync(AccessPolicy policy) => Task.CompletedTask;
        public Task<int> SaveOAuthProviderAsync(OAuthProvider provider) => Task.FromResult(0);
        public Task<bool> DeleteOAuthProviderAsync(int id) => Task.FromResult(false);
        public Task<int> SaveAiProviderAsync(AiProvider provider) => Task.FromResult(0);
        public Task<bool> DeleteAiProviderAsync(int id) => Task.FromResult(false);
        public Task<bool> SaveAuthenticationConfigurationAsync(AuthenticationConfiguration configuration) => Task.FromResult(false);
        public Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm)
            => Task.FromResult<IReadOnlyList<ExternalDirectoryUser>>([]);
        public Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod authMethod, string? searchTerm, int? providerId)
            => Task.FromResult<IReadOnlyList<ExternalDirectoryUser>>([]);
    }

    private sealed class DesignAiTextGenerationService : IAiTextGenerationService
    {
        public Task<string> GenerateSummaryAsync(string sourceContent) => Task.FromResult(string.Empty);
        public Task<string> GenerateIndexAsync(string sourceContent) => Task.FromResult(string.Empty);
        public Task<string> GenerateQuizAsync(string sourceContent, int directQuestions, int multipleChoiceQuestions) => Task.FromResult(string.Empty);
        public Task<string> AnswerQuestionAsync(string question, string sourceContent) => Task.FromResult(string.Empty);
    }

    private sealed class DesignAiProviderProbeService : IAiProviderProbeService
    {
        public Task<IReadOnlyList<string>> ListModelsAsync(AiProvider provider, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<AiProviderProbeResult> TestAsync(AiProvider provider, CancellationToken cancellationToken = default)
            => Task.FromResult(new AiProviderProbeResult(false, string.Empty, []));
    }

    private sealed class DesignDocumentExportService : IDocumentExportService
    {
        public Task ExportToWordAsync(string html, string filePath, bool applyWordFineTune = false, bool refreshImageCache = false) => Task.CompletedTask;
        public Task ExportToPdfAsync(string html, string filePath) => Task.CompletedTask;
    }

    private sealed class DesignExportHistoryService : IExportHistoryService
    {
        public Task RecordAsync(ExportHistoryEntry entry) => Task.CompletedTask;
    }
}
