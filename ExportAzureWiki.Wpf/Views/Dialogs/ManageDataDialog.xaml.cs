using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

/// <summary>
/// Lets the user export, import and selectively clear stored data (saved wikis,
/// AI providers, OAuth providers). Those operations act on the configured
/// database and are admin-only. A separate section lets any signed-in user wipe
/// this machine's local state (the per-user reset that re-runs onboarding).
/// </summary>
public partial class ManageDataDialog : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IWikiCatalogService _wikiCatalog;
    private readonly IAdminCatalogService _adminCatalog;

    public ManageDataDialog(IWikiCatalogService wikiCatalog, IAdminCatalogService adminCatalog, bool isAdmin)
    {
        InitializeComponent();
        _wikiCatalog = wikiCatalog;
        _adminCatalog = adminCatalog;

        ApplyTexts();

        // Data export/import/clear acts on the configured (possibly shared)
        // database, so it is admin-only. The local-machine reset stays available
        // to everyone below.
        DataSection.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        Loaded += async (_, _) => await ReloadCountsAsync();
    }

    private void ApplyTexts()
    {
        Title = AppText.S("wpf.data.title", "Manage Data");
        TitleText.Text = Title;
        DataSectionHeader.Text = AppText.S("wpf.data.section.data", "Data (configured database)");
        SecretsWarning.Text = AppText.S("wpf.data.secrets_warning",
            "The exported file contains secrets (tokens, keys) in plain text. Keep it somewhere safe.");
        BtnExport.Content = AppText.S("wpf.data.export", "Export selected");
        BtnImport.Content = AppText.S("wpf.data.import", "Import...");
        BtnClear.Content = AppText.S("wpf.data.clear", "Clear selected");
        LocalSectionHeader.Text = AppText.S("wpf.data.section.local", "This machine");
        LocalNote.Text = AppText.S("wpf.data.local.note",
            "Clears all local configuration on this machine and closes the app. External databases are not affected.");
        BtnLocalReset.Content = AppText.S("wpf.data.local.button", "Clear all data on this machine");
        BtnClose.Content = AppText.S("common.close", "Close");
    }

    private async Task ReloadCountsAsync()
    {
        try
        {
            var wikis = await _wikiCatalog.LoadAsync();
            var ai = await _adminCatalog.LoadAiProvidersAsync();
            var oauth = await _adminCatalog.LoadOAuthProvidersAsync();

            ChkWikis.Content = CategoryLabel("wpf.data.cat.wikis", "Saved wikis", wikis.Count);
            ChkAi.Content = CategoryLabel("wpf.data.cat.ai", "AI providers", ai.Count);
            ChkOAuth.Content = CategoryLabel("wpf.data.cat.oauth", "OAuth providers", oauth.Count);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(AppText.S("wpf.data.error", "Error: {0}"), ex.Message));
        }
    }

    private static string CategoryLabel(string key, string fallback, int count)
        => $"{AppText.S(key, fallback)}  ({count})";

    private bool AnySelected()
    {
        if (ChkWikis.IsChecked == true || ChkAi.IsChecked == true || ChkOAuth.IsChecked == true)
        {
            return true;
        }

        SetStatus(AppText.S("wpf.data.none_selected", "Select at least one category."));
        return false;
    }

    private async void BtnExport_OnClick(object sender, RoutedEventArgs e)
    {
        if (!AnySelected())
        {
            return;
        }

        try
        {
            var bundle = new AppDataBundle();
            if (ChkWikis.IsChecked == true)
            {
                bundle.Wikis = (await _wikiCatalog.LoadAsync()).ToList();
            }
            if (ChkAi.IsChecked == true)
            {
                bundle.AiProviders = (await _adminCatalog.LoadAiProvidersAsync()).ToList();
            }
            if (ChkOAuth.IsChecked == true)
            {
                bundle.OAuthProviders = (await _adminCatalog.LoadOAuthProvidersAsync()).ToList();
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = $"awikiexport-data-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(bundle, JsonOptions));
            SetStatus(string.Format(AppText.S("wpf.data.export.done", "Exported to: {0}"), dialog.FileName));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(AppText.S("wpf.data.error", "Error: {0}"), ex.Message));
        }
    }

    private async void BtnImport_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var bundle = JsonSerializer.Deserialize<AppDataBundle>(
                await File.ReadAllTextAsync(dialog.FileName), JsonOptions);
            if (bundle == null)
            {
                SetStatus(AppText.S("wpf.data.import.invalid", "Invalid or unreadable file."));
                return;
            }

            var count = 0;
            if (bundle.Wikis is { Count: > 0 })
            {
                await _wikiCatalog.SaveAsync(bundle.Wikis);
                count += bundle.Wikis.Count;
            }
            foreach (var provider in bundle.AiProviders ?? [])
            {
                provider.Id = 0; // insert as new on the target database
                await _adminCatalog.SaveAiProviderAsync(provider);
                count++;
            }
            foreach (var provider in bundle.OAuthProviders ?? [])
            {
                provider.Id = 0;
                await _adminCatalog.SaveOAuthProviderAsync(provider);
                count++;
            }

            await ReloadCountsAsync();
            SetStatus(string.Format(AppText.S("wpf.data.import.done", "Import complete: {0} record(s)."), count));
        }
        catch (JsonException)
        {
            SetStatus(AppText.S("wpf.data.import.invalid", "Invalid or unreadable file."));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(AppText.S("wpf.data.error", "Error: {0}"), ex.Message));
        }
    }

    private async void BtnClear_OnClick(object sender, RoutedEventArgs e)
    {
        if (!AnySelected())
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            AppText.S("wpf.data.clear.confirm", "Delete the selected data from the configured database? This cannot be undone."),
            AppText.S("wpf.data.title", "Manage Data"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var removed = 0;
            if (ChkWikis.IsChecked == true)
            {
                foreach (var wiki in await _wikiCatalog.LoadAsync())
                {
                    if (!string.IsNullOrWhiteSpace(wiki.Id) && await _wikiCatalog.DeleteByIdAsync(wiki.Id))
                    {
                        removed++;
                    }
                }
            }
            if (ChkAi.IsChecked == true)
            {
                foreach (var provider in await _adminCatalog.LoadAiProvidersAsync())
                {
                    if (await _adminCatalog.DeleteAiProviderAsync(provider.Id))
                    {
                        removed++;
                    }
                }
            }
            if (ChkOAuth.IsChecked == true)
            {
                foreach (var provider in await _adminCatalog.LoadOAuthProvidersAsync())
                {
                    if (await _adminCatalog.DeleteOAuthProviderAsync(provider.Id))
                    {
                        removed++;
                    }
                }
            }

            await ReloadCountsAsync();
            SetStatus(string.Format(AppText.S("wpf.data.clear.done", "{0} record(s) removed."), removed));
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(AppText.S("wpf.data.error", "Error: {0}"), ex.Message));
        }
    }

    private void BtnLocalReset_OnClick(object sender, RoutedEventArgs e)
    {
        ExportAzureWiki.Wpf.Services.AppReset.PromptAndReset(this);
    }

    private void BtnClose_OnClick(object sender, RoutedEventArgs e) => Close();

    private void SetStatus(string text) => StatusText.Text = text;
}

/// <summary>Serializable bundle for data export/import.</summary>
public sealed class AppDataBundle
{
    public List<WikiConfiguration> Wikis { get; set; } = [];
    public List<AiProvider> AiProviders { get; set; } = [];
    public List<OAuthProvider> OAuthProviders { get; set; } = [];
}
