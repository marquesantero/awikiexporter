using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Platform.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExportAzureWiki.Tests.Platform;

/// <summary>
/// Smoke test for the DI graph. The previous manual wiring in
/// ServiceRegistry.CreateDefault meant a missing constructor parameter
/// only showed up at runtime in the WPF shell or the CLI; now that
/// composition is the DI container's job, a missing registration is a
/// fast-failing exception we can catch in CI.
/// </summary>
public sealed class PlatformServiceCollectionTests
{
    [Fact]
    public void Container_Resolves_The_Full_App_Service_Set()
    {
        var services = new ServiceCollection();
        services.AddExportAzureWikiPlatform();

        using var provider = services.BuildServiceProvider();
        var set = provider.GetRequiredService<IAppServiceSet>();

        // Every facet of IAppServiceSet must be non-null after the graph
        // is composed. Asserting them individually means a missing
        // registration surfaces with the exact identifier that failed.
        set.Authentication.Should().NotBeNull();
        set.WikiCatalog.Should().NotBeNull();
        set.WikiPageBrowser.Should().NotBeNull();
        set.WikiPageRenderer.Should().NotBeNull();
        set.AdminCatalog.Should().NotBeNull();
        set.AiTextGeneration.Should().NotBeNull();
        set.DocumentExport.Should().NotBeNull();
        set.ExportHistory.Should().NotBeNull();
    }

    [Fact]
    public void Service_Set_Is_Registered_As_A_Singleton()
    {
        var services = new ServiceCollection();
        services.AddExportAzureWikiPlatform();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IAppServiceSet>();
        var second = provider.GetRequiredService<IAppServiceSet>();

        // Singleton semantics matter here: WPF caches IAppServiceSet on
        // the MainViewModel; if the container started handing out new
        // instances, login state would not survive a navigation.
        ReferenceEquals(first, second).Should().BeTrue();
    }
}
