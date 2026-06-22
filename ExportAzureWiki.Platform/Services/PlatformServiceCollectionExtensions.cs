using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Data;
using ExportAzureWiki.Platform.Backend;
using ExportAzureWiki.Services;
using ExportAzureWiki.Services.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace ExportAzureWiki.Platform.Services;

/// <summary>
/// Wires Platform onto a Microsoft.Extensions.DependencyInjection
/// container. The WPF shell and the CLI build their own
/// <see cref="IServiceCollection"/>, call <see cref="AddExportAzureWikiPlatform"/>,
/// and resolve <see cref="IAppServiceSet"/>. Tests do the same with
/// stand-ins for the few infrastructure singletons.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Platform-owned service against the supplied
    /// container. Lifetimes are chosen to match the previous manual
    /// wiring in <c>ServiceRegistry.CreateDefault</c>: everything is a
    /// singleton because the original instances were process-lived.
    /// </summary>
    public static IServiceCollection AddExportAzureWikiPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Infrastructure singletons.
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddSingleton<PasswordHashingService>();
        services.AddSingleton<OAuthProviderFactoryService>();
        services.AddSingleton<WikiServiceFactory>();
        services.AddSingleton<IWikiServiceFactory>(sp => sp.GetRequiredService<WikiServiceFactory>());
        services.AddSingleton<SecurityAuditService>();
        services.AddSingleton<DiagnosticBundleService>();

        // Backends (internal sealed). Each one wraps stateless logic over
        // the connection factory and the helper services above.
        services.AddSingleton<IAuthenticationBackend, AuthenticationBackend>();
        services.AddSingleton<IWikiBackend, WikiBackend>();
        services.AddSingleton<IAdminBackend, AdminBackend>();
        services.AddSingleton<IAiBackend, AiBackend>();
        services.AddSingleton<IExportBackend, ExportBackend>();

        // App-facing services (exposed via IAppServiceSet).
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IWikiCatalogService, WikiCatalogService>();
        services.AddSingleton<IWikiPageBrowserService, WikiPageBrowserService>();
        services.AddSingleton<IWikiPageRenderService>(sp =>
            new WikiPageRenderService(sp.GetRequiredService<IWikiServiceFactory>()));
        services.AddSingleton<IAdminCatalogService, AdminCatalogService>();
        services.AddSingleton<IAiTextGenerationService, AiTextGenerationService>();
        services.AddSingleton<IAiProviderProbeService>(sp =>
            new AiProviderProbeService(sp.GetRequiredService<IDbConnectionFactory>()));
        services.AddSingleton<IDocumentExportService, DocumentExportService>();
        services.AddSingleton<IExportHistoryService>(sp =>
            new ExportHistoryService(sp.GetRequiredService<IDbConnectionFactory>()));

        // Composite that exposes the eight app-facing services in one
        // shape -- the existing IAppServiceSet contract WPF and CLI both
        // consume.
        services.AddSingleton<IAppServiceSet, ServiceRegistry>();

        return services;
    }
}
