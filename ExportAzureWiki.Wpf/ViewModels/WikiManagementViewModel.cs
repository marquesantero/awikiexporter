using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Wpf.Commands;
using System.Windows;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class WikiManagementViewModel : ViewModelBase
{
    private readonly IWikiCatalogService _wikiCatalogService;
    private readonly IWikiPageBrowserService _wikiPageBrowserService;
    private string _status = AppText.S("wpf.wikis.status.ready", "Ready");
    private bool _isLoading;
    private WikiConfiguration? _selected;
    public System.Collections.ObjectModel.ObservableCollection<WikiConfiguration> Wikis { get; } = [];
    public System.Windows.Input.ICommand RefreshCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }
    public event EventHandler? WikisChanged;

    public WikiManagementViewModel(IWikiCatalogService wikiCatalogService, IWikiPageBrowserService wikiPageBrowserService)
    {
        _wikiCatalogService = wikiCatalogService;
        _wikiPageBrowserService = wikiPageBrowserService;
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        DeleteCommand = new RelayCommand(async () => await DeleteAsync(), () => SelectedWiki != null);
    }

    public string HeaderTitle => AppText.S("wpf.wikis.header.title", "Wiki Management");
    public string RefreshText => AppText.S("common.refresh", "Refresh");
    public string NameText => AppText.S("common.name", "Name");
    public string PlatformText => AppText.S("wpf.wikis.platform", "Platform");
    public string OrganizationUrlText => AppText.S("wpf.wikis.organization_url", "Organization URL");
    public string NewText => AppText.S("common.new", "New");
    public string EditText => AppText.S("common.edit", "Edit");
    public string DeleteText => AppText.S("common.delete", "Delete");
    public string OrganizationHeader => AppText.S("wpf.wikis.col.organization", "Organization");
    public string ProjectHeader => AppText.S("wpf.wikis.col.project", "Project");
    public string WikiHeader => AppText.S("wpf.wikis.col.wiki", "Wiki");
    public string RepositoryHeader => AppText.S("wpf.wikis.col.repository", "Repository");
    public string StartPointsHeader => AppText.S("wpf.wikis.col.start_points", "Start Points");

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public WikiConfiguration? SelectedWiki
    {
        get => _selected;
        set
        {
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditSelected));
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        try
        {
            IsLoading = true;
            Status = AppText.S("wpf.wikis.status.loading", "Loading wiki configurations...");
            Wikis.Clear();

            var wikis = await _wikiCatalogService.LoadAsync();
            foreach (var wiki in wikis.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase))
            {
                Wikis.Add(wiki);
            }

            SelectedWiki = Wikis.FirstOrDefault();
            Status = string.Format(AppText.S("wpf.wikis.status.loaded", "Loaded {0} wiki configuration(s)"), Wikis.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.wikis.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public WikiConfiguration CreateDraftForNew()
    {
        Status = AppText.S("wpf.wikis.status.new_mode", "Creating a new wiki configuration.");
        return new WikiConfiguration
        {
            Platform = WikiPlatform.AzureDevOps,
            AuthType = AuthenticationType.PersonalAccessToken,
            IsActive = true
        };
    }

    public WikiConfiguration? CreateDraftFromSelected()
    {
        if (SelectedWiki == null)
        {
            return null;
        }

        var source = SelectedWiki;
        return new WikiConfiguration
        {
            Id = source.Id,
            Name = source.Name,
            Platform = source.Platform,
            BaseUrl = source.BaseUrl,
            AuthType = source.AuthType,
            AuthenticationData = source.AuthenticationData.ToDictionary(k => k.Key, v => v.Value),
            PlatformSpecificData = source.PlatformSpecificData.ToDictionary(k => k.Key, v => v.Value),
            RootPath = source.RootPath,
            IsDefault = source.IsDefault,
            CreatedAt = source.CreatedAt,
            LastUsedAt = source.LastUsedAt,
            IconColor = source.IconColor,
            IsActive = source.IsActive,
            VisibilityScope = source.VisibilityScope,
            OwnerUserId = source.OwnerUserId,
            OwnerDisplayName = source.OwnerDisplayName,
            CreatedByAdmin = source.CreatedByAdmin
        };
    }

    public async Task<bool> SaveFromDialogAsync(WikiConfiguration draft, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            Status = AppText.S("wpf.wikis.status.validation_name", "Name is required.");
            return false;
        }

        try
        {
            IsLoading = true;
            draft.LastUsedAt = DateTime.Now;

            if (isNew)
            {
                Wikis.Add(draft);
                SelectedWiki = draft;
            }
            else
            {
                var current = SelectedWiki;
                if (current == null)
                {
                    Status = AppText.S("wpf.wikis.status.validation_select", "Select a wiki first.");
                    return false;
                }

                var idx = Wikis.IndexOf(current);
                if (idx >= 0)
                {
                    Wikis[idx] = draft;
                    SelectedWiki = draft;
                }
            }

            await _wikiCatalogService.SaveAsync(Wikis.ToList());
            Status = AppText.S("wpf.wikis.status.saved", "Wiki configuration saved.");
            WikisChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.wikis.status.error", "Error: {0}"), ex.Message);
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedWiki == null)
        {
            return;
        }

        var current = SelectedWiki;
        var confirmation = MessageBox.Show(
            string.Format(
                AppText.S("wpf.confirm.delete_wiki.message", "Delete wiki '{0}'? This action cannot be undone."),
                current.Name),
            AppText.S("wpf.confirm.delete_wiki.title", "Confirm Deletion"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            Status = AppText.S("wpf.confirm.delete_wiki.canceled", "Wiki deletion canceled.");
            return;
        }

        try
        {
            IsLoading = true;
            var deleted = await _wikiCatalogService.DeleteByIdAsync(current.Id);
            if (!deleted)
            {
                Status = AppText.S("wpf.wikis.status.delete_failed", "Could not delete selected wiki.");
                return;
            }

            // Drop the deleted wiki's cached content so it does not outlive the
            // configuration (and the access) it came from.
            WikiCacheMaintenance.ClearForWiki(current.Id);

            Wikis.Remove(current);
            SelectedWiki = Wikis.FirstOrDefault();
            if (SelectedWiki == null) { }
            Status = AppText.S("wpf.wikis.status.deleted", "Wiki configuration deleted.");
            WikisChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.wikis.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanEditSelected => SelectedWiki != null;

    public async Task<IReadOnlyList<string>> LoadWikiPathsForStartPointsAsync(WikiConfiguration draftConfiguration)
    {
        var pages = await _wikiPageBrowserService.GetPagesAsync(draftConfiguration);
        return pages
            .Select(p => p.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

