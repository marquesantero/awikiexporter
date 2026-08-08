using System.CommandLine;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Platform;
using ExportAzureWiki.Platform.Notifications;
using ExportAzureWiki.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExportAzureWiki.CLI;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Route Platform notifications to stdout/stderr before any service
        // is instantiated so even bootstrap errors surface in the terminal
        // instead of being dropped by NullUserNotifier.
        UserNotifier.Configure(new ConsoleUserNotifier());

        var provider = PlatformHost.CreateProvider();
        var services = provider.GetRequiredService<IAppServiceSet>();
        var rootCommand = new RootCommand("ExportAzureWiki CLI - export wiki content to HTML, Word, or PDF.");

        var organizationOption = new Option<string>(
            "--organization",
            "Azure DevOps organization URL, for example https://dev.azure.com/myorg")
        {
            IsRequired = true
        };

        var tokenOption = new Option<string>(
            "--token",
            "Personal Access Token for authentication")
        {
            IsRequired = true
        };

        var projectOption = new Option<string>("--project", "Project name")
        {
            IsRequired = true
        };

        var wikiOption = new Option<string>("--wiki", "Wiki name")
        {
            IsRequired = true
        };

        var formatOption = new Option<string>("--format", "Export format")
        {
            IsRequired = true
        }.FromAmong("docx", "html");

        var outputOption = new Option<string>("--output", "Output file path")
        {
            IsRequired = true
        };

        var pagesOption = new Option<string[]>(
            "--pages",
            "Specific wiki page paths to export. Leave empty to export all discovered pages.");

        var exportCommand = new Command("export", "Export wiki content")
        {
            organizationOption,
            tokenOption,
            projectOption,
            wikiOption,
            formatOption,
            outputOption,
            pagesOption
        };

        exportCommand.SetHandler(async context =>
        {
            var configuration = CreateConfiguration(
                context.ParseResult.GetValueForOption(organizationOption)!,
                context.ParseResult.GetValueForOption(tokenOption)!,
                context.ParseResult.GetValueForOption(projectOption)!,
                context.ParseResult.GetValueForOption(wikiOption)!);

            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption)!;
            var pages = context.ParseResult.GetValueForOption(pagesOption) ?? Array.Empty<string>();

            await ExportWikiAsync(services, configuration, format, output, pages);
        });

        var configCommand = new Command("config", "Save a wiki connection in the application catalog")
        {
            organizationOption,
            tokenOption,
            projectOption,
            wikiOption
        };

        configCommand.SetHandler(async context =>
        {
            var configuration = CreateConfiguration(
                context.ParseResult.GetValueForOption(organizationOption)!,
                context.ParseResult.GetValueForOption(tokenOption)!,
                context.ParseResult.GetValueForOption(projectOption)!,
                context.ParseResult.GetValueForOption(wikiOption)!);

            await SaveConfigAsync(services, configuration);
        });

        var diagnoseOutputOption = new Option<string>(
            "--output",
            "Path for the diagnostic bundle .zip")
        {
            IsRequired = true
        };

        var diagnoseCommand = new Command(
            "diagnose",
            "Package an operational bundle (recent logs, app/runtime info, audit summary) without secrets")
        {
            diagnoseOutputOption,
        };

        diagnoseCommand.SetHandler(async context =>
        {
            var output = context.ParseResult.GetValueForOption(diagnoseOutputOption)!;
            var bundleService = provider.GetRequiredService<DiagnosticBundleService>();
            try
            {
                await bundleService.CreateBundleAsync(output, context.GetCancellationToken());
                Console.WriteLine($"Diagnostic bundle written to {output}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to create diagnostic bundle: {ex.Message}");
                context.ExitCode = 1;
            }
        });

        rootCommand.AddCommand(exportCommand);
        rootCommand.AddCommand(configCommand);
        rootCommand.AddCommand(diagnoseCommand);

        return await rootCommand.InvokeAsync(args);
    }

    private static WikiConfiguration CreateConfiguration(string organization, string token, string project, string wiki)
    {
        return new WikiConfiguration
        {
            Name = string.IsNullOrWhiteSpace(wiki) ? project : $"{project} / {wiki}",
            Platform = WikiPlatform.AzureDevOps,
            AuthType = AuthenticationType.PersonalAccessToken,
            OrganizationUrl = organization,
            PersonalAccessToken = token,
            ProjectName = project,
            WikiName = wiki,
            IsDefault = true,
            IsActive = true
        };
    }

    private static async Task SaveConfigAsync(IAppServiceSet services, WikiConfiguration configuration)
    {
        var items = (await services.WikiCatalog.LoadAsync()).ToList();
        var existing = items.FindIndex(item =>
            string.Equals(item.OrganizationUrl, configuration.OrganizationUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ProjectName, configuration.ProjectName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.WikiName, configuration.WikiName, StringComparison.OrdinalIgnoreCase));

        if (configuration.IsDefault)
        {
            foreach (var item in items)
            {
                item.IsDefault = false;
            }
        }

        if (existing >= 0)
        {
            configuration.Id = items[existing].Id;
            configuration.CreatedAt = items[existing].CreatedAt;
            items[existing] = configuration;
        }
        else
        {
            items.Add(configuration);
        }

        await services.WikiCatalog.SaveAsync(items);
        Console.WriteLine("Configuration saved successfully.");
    }

    private static async Task ExportWikiAsync(
        IAppServiceSet services,
        WikiConfiguration configuration,
        string format,
        string output,
        string[] specificPages)
    {
        try
        {
            Console.WriteLine("Starting wiki export...");

            var pagePaths = specificPages
                .Where(page => !string.IsNullOrWhiteSpace(page))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pagePaths.Count == 0)
            {
                Console.WriteLine("Fetching wiki structure...");
                var pages = await services.WikiPageBrowser.GetPagesAsync(configuration);
                pagePaths = pages
                    .Select(page => page.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (pagePaths.Count == 0)
            {
                Console.WriteLine("No pages found to export.");
                return;
            }

            Console.WriteLine($"Rendering {pagePaths.Count} page(s)...");
            var renderedPages = await services.WikiPageRenderer.RenderWikiPagesAsync(
                configuration,
                pagePaths,
                forceRefreshCache: false,
                offlineMode: false);

            if (renderedPages.Count == 0)
            {
                Console.WriteLine("No content generated.");
                return;
            }

            var combinedHtml = CombinePages(renderedPages);
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            Console.WriteLine($"Exporting to {format.ToUpperInvariant()}...");
            switch (format.ToLowerInvariant())
            {
                case "docx":
                    await services.DocumentExport.ExportToWordAsync(combinedHtml, output);
                    break;
                case "html":
                    await File.WriteAllTextAsync(output, combinedHtml);
                    break;
            }

            Console.WriteLine($"Export completed: {output}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during export: {ex.Message}");
            Console.Error.WriteLine(ex);
        }
    }

    private static string CombinePages(IReadOnlyList<RenderedWikiPage> renderedPages)
    {
        if (renderedPages.Count == 1)
        {
            return File.ReadAllText(renderedPages[0].HtmlFilePath);
        }

        var combinedBody = string.Empty;
        var headContent = string.Empty;

        foreach (var pageHtml in renderedPages.Select(page => File.ReadAllText(page.HtmlFilePath)))
        {
            if (string.IsNullOrEmpty(headContent))
            {
                var headMatch = System.Text.RegularExpressions.Regex.Match(pageHtml, @"<head>([\s\S]*?)</head>");
                if (headMatch.Success)
                {
                    headContent = headMatch.Value;
                }
            }

            var bodyMatch = System.Text.RegularExpressions.Regex.Match(pageHtml, @"<body[^>]*>([\s\S]*?)</body>");
            if (bodyMatch.Success)
            {
                combinedBody += bodyMatch.Groups[1].Value;
            }
        }

        return $"""
                <html>
                {headContent}
                <body class="content-body">
                {combinedBody}
                </body>
                </html>
                """;
    }
}
