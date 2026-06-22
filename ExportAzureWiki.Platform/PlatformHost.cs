using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Platform.Bootstrap;
using ExportAzureWiki.Platform.Logging;
using ExportAzureWiki.Platform.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExportAzureWiki.Platform;

/// <summary>
/// Entry point for the WPF shell and the CLI: builds a DI container,
/// boots Platform-wide infrastructure (Serilog, Dapper settings,
/// schema upgrade) and exposes either the composed
/// <see cref="IAppServiceSet"/> facade or the full
/// <see cref="IServiceProvider"/> for callers that need to resolve
/// services outside the facade (CLI tooling, diagnostics).
///
/// Hosts that want to substitute a service for tests or alternative
/// adapters pass a <c>configureServices</c> callback. The callback
/// runs after the default registrations and overrides anything it
/// touches.
/// </summary>
public static class PlatformHost
{
    public static IAppServiceSet CreateServices() => CreateServices(configureServices: null);

    public static IAppServiceSet CreateServices(Action<IServiceCollection>? configureServices)
    {
        var provider = CreateProvider(configureServices);
        return provider.GetRequiredService<IAppServiceSet>();
    }

    public static IServiceProvider CreateProvider() => CreateProvider(configureServices: null);

    public static IServiceProvider CreateProvider(Action<IServiceCollection>? configureServices)
    {
        // Logging must come up before anything else so the rest of the
        // boot path (DB connection, schema migration, providers) emits
        // structured events from the first call.
        PlatformLogging.Initialize();
        StartupInitializer.Initialize();

        var services = new ServiceCollection();
        services.AddExportAzureWikiPlatform();
        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
