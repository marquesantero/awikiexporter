using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Input;
using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Wpf.Commands;
using ExportAzureWiki.Wpf.Services;
using Markdig;
using HtmlAgilityPack;
using Microsoft.Win32;
using ExportAzureWiki;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed partial class WorkspaceViewModel : ViewModelBase
{
    private readonly IWikiCatalogService _wikiCatalogService;
    private readonly IWikiPageBrowserService _wikiPageBrowserService;
    private readonly IWikiPageRenderService _wikiPageRenderService;
    private readonly IAdminCatalogService _adminCatalogService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IDocumentExportService _documentExportService;
    private readonly IExportHistoryService _exportHistoryService;
    private readonly SemaphoreSlim _exportQueueGate = new(1, 1);
    private int _queuedExportCount;
    private bool _isLoading;
    private bool _isLoadingWikis;
    private bool _isLoadingPages;
    private bool _isExporting;
    private bool _isExternalBusy;
    private string _busyMessage = string.Empty;
    private string _status = AppText.S("wpf.workspace.status.ready", "Ready");
    private WikiConfiguration? _selectedWiki;
    private WikiPageNodeViewModel? _selectedPageNode;
    private string _currentPageMarkdown = string.Empty;
    private string _currentPageHtml = string.Empty;
    private string _currentDocumentTitle = string.Empty;
    private DateTime? _currentPageLastUpdated;
    private readonly Dictionary<string, string> _knownPagePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _pageLastUpdatedByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _generatedPageMarkdown = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RenderedWikiPage> _renderedPages = [];
    private int _currentRenderedPageIndex = -1;
    private string _pageNavigationText = string.Empty;
    private bool _isLocalMarkdownMode;
    private string? _currentLocalMarkdownPath;

    // Strictly per-tab state: the "Online" and "Local" tabs each keep their own
    // loaded set. The working fields above always reflect the active tab; on tab
    // change we snapshot the outgoing tab and restore the incoming one, so AI,
    // export and navigation always act on the selected tab's data.
    private readonly WorkspaceTabSession _onlineSession = new();
    private readonly WorkspaceTabSession _localSession = new();
    private int _selectedTabIndex;

    private WorkspacePreferences _preferences = new();
    private bool _preferencesLoaded;

    // Local-folder lazy rendering: the tree is built from the file list up front
    // (instant), and each page is rendered only when first selected (or when an
    // "all pages" export forces the whole set).
    private readonly Dictionary<string, string> _localFolderFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _localFolderOrder = [];
    private bool _localFolderMode;
    private int _additionalPagesCount;
    private bool _includeAdditionalPages = true;
    private bool _refreshCacheBeforeExport;
    private bool _applyWordFineTune;
    private bool _hasConfiguredAiProvider;
    private bool _useDarkMode;
    private ExportScope _selectedScope = ExportScope.CurrentDocument;
    private CodeThemeOption? _selectedCodeTheme;
    private bool _hasEffectiveAdminAccess;
    public Func<string, string, Task<bool>>? PdfPrintHandlerAsync { get; set; }

    /// <summary>
    /// Renders a Mermaid diagram source to a PNG locally (no external service).
    /// Supplied by the view (offscreen WebView2). Used to pre-render diagrams
    /// before Word/PDF export so nothing is sent to mermaid.ink.
    /// </summary>
    public Func<string, Task<byte[]?>>? MermaidRenderHandler { get; set; }
    private static readonly MarkdownPipeline PreviewPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public WorkspaceViewModel(
        IWikiCatalogService wikiCatalogService,
        IWikiPageBrowserService wikiPageBrowserService,
        IWikiPageRenderService wikiPageRenderService,
        IAdminCatalogService adminCatalogService,
        IAuthenticationService authenticationService,
        IDocumentExportService documentExportService,
        IExportHistoryService exportHistoryService)
    {
        _wikiCatalogService = wikiCatalogService;
        _wikiPageBrowserService = wikiPageBrowserService;
        _wikiPageRenderService = wikiPageRenderService;
        _adminCatalogService = adminCatalogService;
        _authenticationService = authenticationService;
        _documentExportService = documentExportService;
        _exportHistoryService = exportHistoryService;
        RefreshCommand = new RelayCommand(async () => await LoadWikisAsync());
        LoadPagesCommand = new RelayCommand(async () => await LoadPagesAsync(), () => SelectedWiki != null);
        LoadMarkdownFileCommand = new RelayCommand(async () => await LoadMarkdownFileAsync());
        LoadMarkdownFolderCommand = new RelayCommand(async () => await LoadMarkdownFolderAsync());
        LoadMarkdownZipCommand = new RelayCommand(async () => await LoadMarkdownZipAsync());
        LoadLocalPagesCommand = new RelayCommand(async () => await LoadLocalPagesAsync());
        ExportWordCommand = new RelayCommand(async () => await ExportWordAsync());
        ExportPdfCommand = new RelayCommand(async () => await ExportPdfAsync());
        PreviousPageCommand = new RelayCommand(async () => await NavigatePreviousPageAsync(), () => CanNavigatePrevious);
        NextPageCommand = new RelayCommand(async () => await NavigateNextPageAsync(), () => CanNavigateNext);

        LoadCodeThemes();
        ApplyPreferences();
    }

    /// <summary>
    /// Restores persisted UI preferences (dark mode, code theme, active tab,
    /// offline export) into the backing fields without triggering a save, then
    /// arms <see cref="SavePreferences"/> for subsequent user changes.
    /// </summary>
    private void ApplyPreferences()
    {
        _preferences = WorkspacePreferences.Load();

        _useDarkMode = _preferences.DarkMode;
        ThemeManager.DarkModeCheckBox = _preferences.DarkMode;
        OnPropertyChanged(nameof(UseDarkMode));

        if (!string.IsNullOrWhiteSpace(_preferences.CodeThemeFilePath))
        {
            var match = CodeThemes.FirstOrDefault(
                t => string.Equals(t.FilePath, _preferences.CodeThemeFilePath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _selectedCodeTheme = match;
                ThemeManager.SelectedTheme = match.FilePath;
                OnPropertyChanged(nameof(SelectedCodeTheme));
            }
        }

        _offlineExport = _preferences.OfflineExport;
        OnPropertyChanged(nameof(OfflineExport));

        _selectedTabIndex = _preferences.LastTabIndex == 1 ? 1 : 0;
        OnPropertyChanged(nameof(SelectedTabIndex));

        _preferencesLoaded = true;
    }

    private void SavePreferences()
    {
        if (!_preferencesLoaded)
        {
            return;
        }

        _preferences.DarkMode = _useDarkMode;
        _preferences.CodeThemeFilePath = _selectedCodeTheme?.FilePath;
        _preferences.OfflineExport = _offlineExport;
        _preferences.LastTabIndex = _selectedTabIndex;
        _preferences.LastWikiId = _selectedWiki?.Id;
        _preferences.Save();
    }

    public string Title => AppText.S("wpf.workspace.title", "Workspace");
    public string Description => AppText.S("wpf.workspace.description", "Area for Wiki tree, preview, export, and AI actions.");
    public string HeaderTitle => AppText.S("wpf.workspace.header.title", "Migration Workspace");
    public string ConfiguredWikisText => AppText.S("wpf.workspace.configured_wikis", "Configured Wikis");
    public string RefreshText => AppText.S("common.refresh", "Refresh");
    public string LoadPagesText => AppText.S("wpf.workspace.load_pages", "Load Pages");
    public string WikiDetailsText => AppText.S("wpf.workspace.wiki_details", "Wiki Details");
    public string LoadFileText => AppText.S("wpf.workspace.load_file", "Open .md File");
    public string LoadFolderText => AppText.S("wpf.workspace.load_folder", "Open folder");
    public string LoadZipText => AppText.S("wpf.workspace.load_zip", "Open .zip");
    public string OnlineTabText => AppText.S("wpf.workspace.tab.online", "Online");
    public string LocalTabText => AppText.S("wpf.workspace.tab.local", "Local");
    public string LocalEmptyHintText => AppText.S("wpf.workspace.local.empty_hint", "Open a file or folder to list Markdown here.");
    public string SearchPlaceholderText => AppText.S("wpf.workspace.search.placeholder", "Filter pages...");
    public string BusyTitleText => AppText.S("wpf.workspace.busy.title", "Processing");

    private string _onlineFilterText = string.Empty;
    public string OnlineFilterText
    {
        get => _onlineFilterText;
        set
        {
            if (_onlineFilterText == value)
            {
                return;
            }

            _onlineFilterText = value;
            OnPropertyChanged();
            ApplyTreeFilter(PageTree, value, n => n.Title, n => n.Children, (n, v) => n.IsVisible = v);
        }
    }

    private string _localFilterText = string.Empty;
    public string LocalFilterText
    {
        get => _localFilterText;
        set
        {
            if (_localFilterText == value)
            {
                return;
            }

            _localFilterText = value;
            OnPropertyChanged();
            ApplyTreeFilter(LocalPageTree, value, n => n.Title, n => n.Children, (n, v) => n.IsVisible = v);
        }
    }

    /// <summary>
    /// Sets IsVisible on every node so that a node shows when it (or any
    /// descendant) matches the filter. Empty filter makes everything visible.
    /// Returns true if any node at this level is visible.
    /// </summary>
    private static bool ApplyTreeFilter<T>(
        IEnumerable<T> nodes,
        string filter,
        Func<T, string> title,
        Func<T, IEnumerable<T>> children,
        Action<T, bool> setVisible)
    {
        var anyVisible = false;
        foreach (var node in nodes)
        {
            var childMatch = ApplyTreeFilter(children(node), filter, title, children, setVisible);
            var selfMatch = string.IsNullOrWhiteSpace(filter)
                || (title(node)?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
            var visible = selfMatch || childMatch;
            setVisible(node, visible);
            anyVisible |= visible;
        }

        return anyVisible;
    }

    /// <summary>
    /// 0 = Online tab, 1 = Local tab. Switching tabs snapshots the current
    /// working state into the outgoing tab's session and restores the incoming
    /// tab's session, so IA/export/navigation follow the selected tab.
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex == value)
            {
                return;
            }

            if (IsLoading)
            {
                // Reject a tab switch mid-load and revert the TabControl.
                OnPropertyChanged();
                return;
            }

            SaveSession(_selectedTabIndex == 1 ? _localSession : _onlineSession);
            _selectedTabIndex = value;
            OnPropertyChanged();
            RestoreSession(_selectedTabIndex == 1 ? _localSession : _onlineSession);
            SavePreferences();
        }
    }
    public string CurrentSourceText => AppText.S("wpf.workspace.current_source", "Current source:");
    public string LastUpdatedText => AppText.S("wpf.workspace.last_updated", "Last updated:");
    public string MarkdownTabText => AppText.S("wpf.workspace.tab.markdown", "Markdown");
    public string PreviewTabText => AppText.S("wpf.workspace.tab.preview", "Preview");
    public string CurrentDocumentText => AppText.S("wpf.export.scope.current", "Current page/file");
    public string AllLoadedPagesText => AppText.S("wpf.export.scope.all_loaded", "All loaded wiki pages");
    public string IncludeAiPagesText => AppText.S("wpf.export.include_ai_pages", "Include AI extra pages");
    public string RefreshCacheText => AppText.S("main.option.refresh_cache", "Refresh Cache");
    public string OfflineExportText => AppText.S("wpf.export.offline", "Offline export (cache only)");
    public string ApplyWordFineTuneText => AppText.S("wpf.export.word_post_processing.experimental", "Word post-processing (experimental)");
    public string DarkModeText => AppText.S("main.option.dark_mode", "Dark Mode");
    public string CodeThemeText => AppText.S("main.option.code_theme", "Code Theme");
    public string ExportWordText => AppText.S("wpf.export.word", "Export to Word");
    public string ExportPdfText => AppText.S("wpf.export.pdf", "Export to PDF");
    public string PreviousPageText => AppText.S("main.button.previous", "Previous");
    public string NextPageText => AppText.S("main.button.next", "Next");

    public ObservableCollection<WikiConfiguration> Wikis { get; } = [];
    public ObservableCollection<WikiPageNodeViewModel> PageTree { get; } = [];
    public ObservableCollection<LocalPageNodeViewModel> LocalPageTree { get; } = [];
    public ObservableCollection<AdditionalExportPage> AdditionalExportPages { get; } = [];
    public ObservableCollection<CodeThemeOption> CodeThemes { get; } = [];

    public WikiConfiguration? SelectedWiki
    {
        get => _selectedWiki;
        set
        {
            _selectedWiki = value;
            OnPropertyChanged();
            (LoadPagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            SavePreferences();
            if (_selectedWiki != null && !IsLoading)
            {
                _ = LoadTreeForSelectedWikiAsync();
            }
        }
    }

    public WikiPageNodeViewModel? SelectedPageNode
    {
        get => _selectedPageNode;
        private set
        {
            _selectedPageNode = value;
            OnPropertyChanged();
        }
    }

    public string CurrentPageMarkdown
    {
        get => _currentPageMarkdown;
        private set
        {
            _currentPageMarkdown = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLoadedPage));
        }
    }

    public string CurrentPageHtml
    {
        get => _currentPageHtml;
        private set
        {
            _currentPageHtml = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLoadedPage));
        }
    }

    public bool HasLoadedPage =>
        !string.IsNullOrWhiteSpace(CurrentPageMarkdown) ||
        !string.IsNullOrWhiteSpace(CurrentPageHtml);

    public string CurrentDocumentTitle
    {
        get => _currentDocumentTitle;
        private set
        {
            _currentDocumentTitle = value;
            OnPropertyChanged();
        }
    }

    public string CurrentPageLastUpdatedDisplay
    {
        get
        {
            if (_currentPageLastUpdated is null)
            {
                return AppText.S("wpf.workspace.last_updated.not_available", "N/A");
            }

            return _currentPageLastUpdated.Value.ToString("dd/MM/yyyy HH:mm:ss");
        }
    }

    public int AdditionalPagesCount
    {
        get => _additionalPagesCount;
        private set
        {
            _additionalPagesCount = value;
            OnPropertyChanged();
        }
    }

    public string PageNavigationText
    {
        get => _pageNavigationText;
        private set
        {
            _pageNavigationText = value;
            OnPropertyChanged();
        }
    }

    public bool IncludeAdditionalPages
    {
        get => _includeAdditionalPages;
        set
        {
            _includeAdditionalPages = value;
            OnPropertyChanged();
        }
    }

    public bool RefreshCacheBeforeExport
    {
        get => _refreshCacheBeforeExport;
        set
        {
            _refreshCacheBeforeExport = value;
            OnPropertyChanged();
        }
    }

    private bool _showLoadLocalPagesButton;

    /// <summary>
    /// True when a multi-page local folder/zip is loaded as a tree only and the
    /// user must pick pages and click "Load pages". Hidden for single-page loads.
    /// </summary>
    public bool ShowLoadLocalPagesButton
    {
        get => _showLoadLocalPagesButton;
        private set
        {
            if (_showLoadLocalPagesButton == value)
            {
                return;
            }

            _showLoadLocalPagesButton = value;
            OnPropertyChanged();
        }
    }

    private bool _offlineExport;

    /// <summary>
    /// When checked, exports use only cached remote images and make no network
    /// calls (reproducible/offline output; missing images are skipped).
    /// </summary>
    public bool OfflineExport
    {
        get => _offlineExport;
        set
        {
            _offlineExport = value;
            OnPropertyChanged();
            SavePreferences();
        }
    }

    public bool ApplyWordFineTune
    {
        get => _applyWordFineTune;
        set
        {
            _applyWordFineTune = CanApplyWordFineTune && value;
            OnPropertyChanged();
        }
    }

    public bool IsWordFineTuneVisible => _hasConfiguredAiProvider;

    public bool CanApplyWordFineTune => IsWordFineTuneVisible && IsCurrentScope;

    public bool UseDarkMode
    {
        get => _useDarkMode;
        set
        {
            _useDarkMode = value;
            ThemeManager.DarkModeCheckBox = value;
            OnPropertyChanged();
            RefreshPreviewStyling();
            SavePreferences();
        }
    }

    public CodeThemeOption? SelectedCodeTheme
    {
        get => _selectedCodeTheme;
        set
        {
            _selectedCodeTheme = value;
            ThemeManager.SelectedTheme = value?.FilePath;
            OnPropertyChanged();
            RefreshPreviewStyling();
            SavePreferences();
        }
    }

    public ExportScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            _selectedScope = value;
            if (_selectedScope != ExportScope.CurrentDocument && _applyWordFineTune)
            {
                _applyWordFineTune = false;
                OnPropertyChanged(nameof(ApplyWordFineTune));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurrentScope));
            OnPropertyChanged(nameof(IsAllLoadedPagesScope));
            OnPropertyChanged(nameof(CanApplyWordFineTune));
        }
    }

    public bool IsCurrentScope
    {
        get => SelectedScope == ExportScope.CurrentDocument;
        set
        {
            if (value)
            {
                SelectedScope = ExportScope.CurrentDocument;
            }
        }
    }

    public bool IsAllLoadedPagesScope
    {
        get => SelectedScope == ExportScope.AllLoadedPages;
        set
        {
            if (value)
            {
                SelectedScope = ExportScope.AllLoadedPages;
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoadingWikis
    {
        get => _isLoadingWikis;
        private set
        {
            _isLoadingWikis = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(ShowBusyFeedback));
        }
    }

    public bool IsLoadingPages
    {
        get => _isLoadingPages;
        private set
        {
            _isLoadingPages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(ShowBusyFeedback));
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            _isExporting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(ShowBusyFeedback));
            (ExportWordCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportPdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy => IsLoadingWikis || IsLoadingPages || IsExporting || _isExternalBusy;
    public bool ShowBusyFeedback => IsBusy && !string.IsNullOrWhiteSpace(BusyMessage);
    public int QueuedExportCount
    {
        get => _queuedExportCount;
        private set
        {
            _queuedExportCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasQueuedExports));
        }
    }

    public bool HasQueuedExports => QueuedExportCount > 0;

    public string BusyMessage
    {
        get => _busyMessage;
        private set
        {
            _busyMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBusyFeedback));
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public void SetExternalStatus(string status)
    {
        Status = status;
    }

    public void SetExternalBusy(string message)
    {
        _isExternalBusy = true;
        BusyMessage = message;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowBusyFeedback));
    }

    public void ClearExternalBusy()
    {
        _isExternalBusy = false;
        if (!IsLoadingWikis && !IsLoadingPages && !IsExporting)
        {
            BusyMessage = string.Empty;
        }

        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowBusyFeedback));
    }

    private void SetExportPhase(string message)
    {
        IsExporting = true;
        BusyMessage = message;
        Status = message;
    }

    private void ClearExportPhase()
    {
        if (QueuedExportCount == 0)
        {
            IsExporting = false;
            BusyMessage = string.Empty;
        }
    }

    public void SetAiProviderAvailability(bool available)
    {
        if (_hasConfiguredAiProvider == available)
        {
            return;
        }

        _hasConfiguredAiProvider = available;
        OnPropertyChanged(nameof(IsWordFineTuneVisible));
        OnPropertyChanged(nameof(CanApplyWordFineTune));

        if (!_hasConfiguredAiProvider && _applyWordFineTune)
        {
            _applyWordFineTune = false;
            OnPropertyChanged(nameof(ApplyWordFineTune));
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand LoadPagesCommand { get; }
    public ICommand LoadMarkdownFileCommand { get; }
    public ICommand LoadMarkdownFolderCommand { get; }
    public ICommand LoadMarkdownZipCommand { get; }
    public ICommand LoadLocalPagesCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand ExportWordCommand { get; }
    public ICommand ExportPdfCommand { get; }

    public bool CanNavigatePrevious => _currentRenderedPageIndex > 0;
    public bool CanNavigateNext => _currentRenderedPageIndex >= 0 && _currentRenderedPageIndex < _renderedPages.Count - 1;

    public async Task LoadWikisAsync()
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            IsLoadingWikis = true;
            BusyMessage = AppText.S("wpf.workspace.busy.loading_wikis", "Loading wikis...");
            Status = AppText.S("wpf.workspace.status.loading_wikis", "Loading wikis...");
            Wikis.Clear();
            PageTree.Clear();
            CurrentPageMarkdown = string.Empty;
            CurrentPageHtml = string.Empty;
            CurrentDocumentTitle = string.Empty;
            _currentPageLastUpdated = null;
            OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
            AdditionalExportPages.Clear();
            AdditionalPagesCount = 0;
            _knownPagePaths.Clear();
            _pageLastUpdatedByPath.Clear();
            _generatedPageMarkdown.Clear();
            _renderedPages.Clear();
            _currentRenderedPageIndex = -1;
            _isLocalMarkdownMode = false;
            _currentLocalMarkdownPath = null;
            PageNavigationText = string.Empty;
            RaiseNavigationStateChanged();

            _hasEffectiveAdminAccess = await ResolveEffectiveAdminAsync();
            var wikis = await _wikiCatalogService.LoadAsync();
            var filtered = await ApplyWikiAccessFilterAsync(wikis.Where(w => w.IsActive));
            foreach (var wiki in filtered)
            {
                Wikis.Add(wiki);
            }

            // Restore the last-used wiki when still available; otherwise the first.
            SelectedWiki = (!string.IsNullOrWhiteSpace(_preferences.LastWikiId)
                    ? Wikis.FirstOrDefault(w => string.Equals(w.Id, _preferences.LastWikiId, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? Wikis.FirstOrDefault();
            if (SelectedWiki != null)
            {
                await LoadTreeForSelectedWikiAsync();
            }
            Status = string.Format(
                AppText.S("wpf.workspace.status.loaded_wikis_count", "Loaded {0} wiki(s)"),
                Wikis.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_loading_wikis", "Error loading wikis: {0}"),
                ex.Message);
        }
        finally
        {
            IsLoading = false;
            // Always clear the wiki-loading flag here. When no wiki is configured
            // SelectedWiki is null and the tree-load path (which otherwise ends
            // the busy state) never runs, so the spinner would hang forever.
            IsLoadingWikis = false;
            BusyMessage = string.Empty;
        }
    }

    public async Task LoadPagesAsync()
    {
        if (IsLoading || SelectedWiki == null)
        {
            return;
        }

        try
        {
            IsLoading = true;
            IsLoadingPages = true;
            BusyMessage = AppText.S("wpf.workspace.busy.loading_pages", "Loading pages...");
            Status = AppText.S("wpf.workspace.status.loading_pages", "Loading pages...");
            var selectedPaths = FlattenCheckedPaths(PageTree)
                .Select(path => ResolveKnownPath(path) ?? path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedPaths.Count == 0)
            {
                Status = AppText.S("wpf.workspace.status.no_checked_pages", "Select one or more pages in the tree.");
                return;
            }
            _isLocalMarkdownMode = false;
            _currentLocalMarkdownPath = null;
            _generatedPageMarkdown.Clear();
            _renderedPages.Clear();
            _currentRenderedPageIndex = -1;

            var renderedPages = await _wikiPageRenderService.RenderWikiPagesAsync(
                SelectedWiki,
                selectedPaths,
                forceRefreshCache: RefreshCacheBeforeExport,
                offlineMode: false);

            _renderedPages.AddRange(renderedPages);
            if (_renderedPages.Count == 0)
            {
                CurrentPageMarkdown = string.Empty;
                CurrentPageHtml = string.Empty;
                CurrentDocumentTitle = string.Empty;
                _currentPageLastUpdated = null;
                OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
                PageNavigationText = string.Empty;
                RaiseNavigationStateChanged();
                Status = AppText.S("wpf.workspace.status.no_checked_content", "No content found for selected pages.");
                return;
            }

            await LoadRenderedPageAsync(0);
            Status = string.Format(
                AppText.S("wpf.workspace.status.loaded_checked_pages", "{0} selected page(s) loaded"),
                _renderedPages.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_loading_pages", "Error loading pages: {0}"),
                ex.Message);
        }
        finally
        {
            IsLoadingPages = false;
            BusyMessage = string.Empty;
            IsLoading = false;
        }
    }

    public async Task SyncWikisCatalogAsync()
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            IsLoadingWikis = true;
            BusyMessage = AppText.S("wpf.workspace.busy.syncing_wikis", "Syncing wikis...");
            Status = AppText.S("wpf.workspace.status.syncing_wikis", "Syncing wikis...");

            var previousSelectedWikiId = SelectedWiki?.Id;

            _hasEffectiveAdminAccess = await ResolveEffectiveAdminAsync();
            var all = await _wikiCatalogService.LoadAsync();
            var filtered = await ApplyWikiAccessFilterAsync(all.Where(w => w.IsActive));
            var list = filtered.ToList();

            Wikis.Clear();
            foreach (var wiki in list)
            {
                Wikis.Add(wiki);
            }

            var matched = !string.IsNullOrWhiteSpace(previousSelectedWikiId)
                ? Wikis.FirstOrDefault(w => w.Id == previousSelectedWikiId)
                : null;

            var selectedWasRemoved = !string.IsNullOrWhiteSpace(previousSelectedWikiId) && matched == null;

            SelectedWiki = matched ?? Wikis.FirstOrDefault();

            if (selectedWasRemoved)
            {
                ClearLoadedState();
                if (SelectedWiki != null)
                {
                    await LoadTreeForSelectedWikiAsync();
                }

                Status = AppText.S("wpf.workspace.status.selected_wiki_removed",
                    "Selected wiki was removed. Workspace state was cleared.");
                return;
            }

            if (SelectedWiki != null && PageTree.Count == 0)
            {
                await LoadTreeForSelectedWikiAsync();
            }

            Status = string.Format(
                AppText.S("wpf.workspace.status.synced_wikis_count", "Wikis synchronized ({0})"),
                Wikis.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_syncing_wikis", "Error syncing wikis: {0}"),
                ex.Message);
        }
        finally
        {
            IsLoadingWikis = false;
            BusyMessage = string.Empty;
            IsLoading = false;
        }
    }

    private async Task LoadTreeForSelectedWikiAsync()
    {
        if (SelectedWiki == null)
        {
            return;
        }

        try
        {
            IsLoading = true;
            IsLoadingPages = true;
            BusyMessage = AppText.S("wpf.workspace.busy.loading_tree", "Loading wiki tree...");
            Status = AppText.S("wpf.workspace.status.loading_pages", "Loading pages...");
            // The Local tab's tree (LocalPageTree) is a per-tab collection, not
            // part of the swapped session, so the Online load must NOT clear it
            // -- doing so left the Local tree empty after switching back while its
            // pages (restored from the session) stayed. The folder fields below
            // are live working state and are safe to reset: the Local values are
            // already preserved in _localSession.
            PageTree.Clear();
            CurrentPageMarkdown = string.Empty;
            CurrentPageHtml = string.Empty;
            CurrentDocumentTitle = string.Empty;
            _currentPageLastUpdated = null;
            OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
            PageNavigationText = string.Empty;
            _generatedPageMarkdown.Clear();
            _pageLastUpdatedByPath.Clear();
            _renderedPages.Clear();
            _localFolderMode = false;
            ShowLoadLocalPagesButton = false;
            _localFolderFiles.Clear();
            _localFolderOrder.Clear();
            _currentRenderedPageIndex = -1;
            _currentRenderedPageIndex = -1;
            RaiseNavigationStateChanged();
            SelectedPageNode = null;

            var pages = await _wikiPageBrowserService.GetPagesAsync(SelectedWiki);
            RegisterKnownPaths(pages.Select(p => p.Path));
            RegisterKnownLastUpdated(pages);
            var startPoints = await GetEffectiveStartPointsAsync();
            var filteredPaths = FilterPathsByStartPoints(pages.Select(p => p.Path), startPoints);
            var tree = BuildTree(filteredPaths, startPoints);
            if (startPoints.Any(sp => !string.Equals(sp, "/", StringComparison.Ordinal)))
            {
                CheckAll(tree);
            }

            foreach (var node in tree)
            {
                PageTree.Add(node);
            }

            // A single-page wiki opens immediately; otherwise the user picks
            // pages in the tree and clicks "Load pages". Release the loading
            // guard so the nested LoadPagesAsync (which bails when IsLoading) runs.
            if (filteredPaths.Count == 1)
            {
                CheckAll(tree);
                IsLoading = false;
                IsLoadingPages = false;
                await LoadPagesAsync();
                return;
            }

            Status = string.Format(
                AppText.S("wpf.workspace.status.loaded_pages_count", "Loaded {0} page(s)"),
                filteredPaths.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_loading_pages", "Error loading pages: {0}"),
                ex.Message);
        }
        finally
        {
            IsLoadingPages = false;
            BusyMessage = string.Empty;
            IsLoading = false;
        }
    }

    public async Task SelectPageAsync(WikiPageNodeViewModel? node)
    {
        SelectedPageNode = node;
        await Task.CompletedTask;
    }


    private static string WrapPreviewHtml(string body)
    {
        var isDarkMode = ThemeManager.DarkModeCheckBox;
        var bodyBg = isDarkMode ? "#1e1e1e" : "#ffffff";
        var bodyColor = isDarkMode ? "#d4d4d4" : "#1f1f1f";
        var preBg = isDarkMode ? "#2a2a2a" : "#f3f5f7";
        var tableBorder = isDarkMode ? "#4a4a4a" : "#d1d9e0";
        var quoteBorder = isDarkMode ? "#6AAAF7" : "#0F6CBD";
        var quoteColor = isDarkMode ? "#cfdaf0" : "#3F4A59";

        return "<!doctype html><html><head><meta charset=\"utf-8\" />"
            + "<style>"
            + $"body {{ font-family: Segoe UI, Arial, sans-serif; margin: 16px; color: {bodyColor}; background:{bodyBg}; }}"
            + $"pre {{ background: {preBg}; padding: 10px; border-radius: 8px; overflow: auto; }}"
            + "code { font-family: Consolas, monospace; }"
            + "table { border-collapse: collapse; width: 100%; }"
            + $"th, td {{ border: 1px solid {tableBorder}; padding: 6px; text-align: left; vertical-align: top; }}"
            + $"blockquote {{ border-left: 4px solid {quoteBorder}; margin-left: 0; padding-left: 10px; color: {quoteColor}; }}"
            + "</style></head><body>" + body + "</body></html>";
    }

    private WikiMarkdownDialect ResolveCurrentMarkdownDialect()
        => SelectedWiki?.Platform switch
        {
            WikiPlatform.AzureDevOps => WikiMarkdownDialect.AzureDevOps,
            WikiPlatform.GitHub => WikiMarkdownDialect.GitHub,
            WikiPlatform.GitLab => WikiMarkdownDialect.GitLab,
            WikiPlatform.Bitbucket => WikiMarkdownDialect.Bitbucket,
            _ => WikiMarkdownDialect.Generic
        };

    private static string RenderMarkdownPreviewFragment(string markdown, WikiMarkdownDialect dialect)
    {
        var normalized = HtmlContentGenerator.NormalizeMarkdownForDialect(markdown ?? string.Empty, dialect);
        return Markdown.ToHtml(normalized, PreviewPipeline);
    }

    private async Task LoadRenderedPageAsync(int index)
    {
        if (index < 0 || index >= _renderedPages.Count)
        {
            return;
        }

        _currentRenderedPageIndex = index;
        var page = _renderedPages[index];
        CurrentDocumentTitle = page.Path;
        PageNavigationText = $"{index + 1}/{_renderedPages.Count}";
        _currentPageLastUpdated = TryResolveCurrentPageLastUpdated(page);
        OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));

        if (!string.IsNullOrWhiteSpace(page.HtmlFilePath) && File.Exists(page.HtmlFilePath))
        {
            CurrentPageHtml = await SecureCacheFile.ReadTextAsync(page.HtmlFilePath);
        }
        else
        {
            CurrentPageHtml = string.Empty;
        }

        if (_generatedPageMarkdown.TryGetValue(page.Path ?? string.Empty, out var generatedMarkdown))
        {
            CurrentPageMarkdown = generatedMarkdown;
        }
        else if (!_isLocalMarkdownMode && SelectedWiki != null && !string.IsNullOrWhiteSpace(page.Path))
        {
            var content = await GetPageContentWithPathFallbackAsync(SelectedWiki, page.Path);
            CurrentPageMarkdown = content?.Content ?? string.Empty;
            if (content?.LastModified is { } contentLastModified && contentLastModified != default)
            {
                RegisterPathLastUpdated(page.Path, contentLastModified);
            }
        }
        else if (_isLocalMarkdownMode && !string.IsNullOrWhiteSpace(_currentLocalMarkdownPath) && File.Exists(_currentLocalMarkdownPath))
        {
            CurrentPageMarkdown = await File.ReadAllTextAsync(_currentLocalMarkdownPath);
        }
        else
        {
            CurrentPageMarkdown = string.Empty;
        }

        RaiseNavigationStateChanged();
    }

    public async Task AddGeneratedPageToPreviewAsync(string title, string markdownContent)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? AppText.S("wpf.workspace.additional_page.default_title", "AI Generated Page")
            : title.Trim();

        var normalizedMarkdown = markdownContent ?? string.Empty;
        var htmlFragment = RenderMarkdownPreviewFragment(normalizedMarkdown, ResolveCurrentMarkdownDialect());
        var fullHtml = WrapPreviewHtml(htmlFragment);

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExportAzureWiki",
            "Cache",
            "GeneratedPages");
        Directory.CreateDirectory(cacheDir);

        var safeName = new string(normalizedTitle
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "ai_page";
        }

        var fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Path.Combine(cacheDir, fileName);
        await File.WriteAllTextAsync(filePath, fullHtml);

        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var virtualPath = $"/AI/{normalizedTitle} [{stamp}]";
        _generatedPageMarkdown[virtualPath] = normalizedMarkdown;
        RegisterPathLastUpdated(virtualPath, DateTime.Now);
        _renderedPages.Add(new RenderedWikiPage
        {
            Path = virtualPath,
            HtmlFilePath = filePath
        });

        await LoadRenderedPageAsync(_renderedPages.Count - 1);
        Status = AppText.S("wpf.workspace.status.generated_page_added", "Generated page added to preview.");
    }

    private async Task NavigatePreviousPageAsync()
    {
        if (!CanNavigatePrevious)
        {
            return;
        }

        await LoadRenderedPageAsync(_currentRenderedPageIndex - 1);
    }

    private async Task NavigateNextPageAsync()
    {
        if (!CanNavigateNext)
        {
            return;
        }

        await LoadRenderedPageAsync(_currentRenderedPageIndex + 1);
    }

    private void RaiseNavigationStateChanged()
    {
        OnPropertyChanged(nameof(CanNavigatePrevious));
        OnPropertyChanged(nameof(CanNavigateNext));
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void LoadCodeThemes()
    {
        CodeThemes.Clear();
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var pathTheme = Path.Combine(baseDirectory, "style", "styles");
        if (!Directory.Exists(pathTheme))
        {
            return;
        }

        var themeFiles = Directory.GetFiles(pathTheme, "*.min.css")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in themeFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file).Replace(".min", "", StringComparison.OrdinalIgnoreCase);
            var displayName = string.Join(" ",
                fileName
                    .Replace("-", " ", StringComparison.Ordinal)
                    .Replace("_", " ", StringComparison.Ordinal)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant()));

            CodeThemes.Add(new CodeThemeOption
            {
                ThemeName = displayName,
                FilePath = file
            });
        }

        SelectedCodeTheme = CodeThemes.FirstOrDefault(t =>
            string.Equals(t.ThemeName, "Default", StringComparison.OrdinalIgnoreCase))
            ?? CodeThemes.FirstOrDefault();

        UseDarkMode = ThemeManager.DarkModeCheckBox;
    }

    private void RefreshPreviewStyling()
    {
        if (_renderedPages.Count == 0 || IsLoading)
        {
            return;
        }

        _ = RefreshRenderedContentForThemeAsync();
    }

    private async Task RefreshRenderedContentForThemeAsync()
    {
        try
        {
            IsLoading = true;
            IsLoadingPages = true;
            BusyMessage = AppText.S("wpf.workspace.busy.refreshing_preview", "Refreshing preview...");
            var targetIndex = Math.Clamp(_currentRenderedPageIndex, 0, Math.Max(0, _renderedPages.Count - 1));

            if (_localFolderMode)
            {
                // Folder/zip mode: re-render every already-rendered page under the
                // new theme (the rendered cache is keyed by a fingerprint that
                // includes dark mode + code theme, so each produces a fresh file).
                var currentRel = (targetIndex >= 0 && targetIndex < _renderedPages.Count)
                    ? _renderedPages[targetIndex].Path
                    : null;
                var rels = _renderedPages
                    .Select(p => p.Path)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _renderedPages.Clear();
                foreach (var rel in rels)
                {
                    if (_localFolderFiles.TryGetValue(rel!, out var abs) && File.Exists(abs))
                    {
                        var r = await _wikiPageRenderService.RenderLocalMarkdownAsync(abs);
                        if (r != null && !string.IsNullOrWhiteSpace(r.HtmlFilePath))
                        {
                            r.Path = rel;
                            _renderedPages.Add(r);
                        }
                    }
                }

                targetIndex = currentRel != null
                    ? Math.Max(0, _renderedPages.FindIndex(p => string.Equals(p.Path, currentRel, StringComparison.OrdinalIgnoreCase)))
                    : 0;
            }
            else if (_isLocalMarkdownMode)
            {
                if (!string.IsNullOrWhiteSpace(_currentLocalMarkdownPath) && File.Exists(_currentLocalMarkdownPath))
                {
                    var rendered = await _wikiPageRenderService.RenderLocalMarkdownAsync(_currentLocalMarkdownPath);
                    _renderedPages.Clear();
                    if (rendered != null)
                    {
                        _renderedPages.Add(rendered);
                        targetIndex = 0;
                    }
                }
            }
            else if (SelectedWiki != null)
            {
                var selectedPaths = FlattenCheckedPaths(PageTree)
                    .Select(path => ResolveKnownPath(path) ?? path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (selectedPaths.Count > 0)
                {
                    var rendered = await _wikiPageRenderService.RenderWikiPagesAsync(
                        SelectedWiki,
                        selectedPaths,
                        forceRefreshCache: true,
                        offlineMode: false);
                    _renderedPages.Clear();
                    _renderedPages.AddRange(rendered);
                    targetIndex = Math.Min(targetIndex, Math.Max(0, _renderedPages.Count - 1));
                }
            }

            if (_renderedPages.Count > 0)
            {
                await LoadRenderedPageAsync(targetIndex);
            }
            else
            {
                CurrentPageHtml = string.Empty;
                CurrentPageMarkdown = string.Empty;
                CurrentDocumentTitle = string.Empty;
                _currentPageLastUpdated = null;
                OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
                PageNavigationText = string.Empty;
                RaiseNavigationStateChanged();
            }
        }
        finally
        {
            IsLoadingPages = false;
            BusyMessage = string.Empty;
            IsLoading = false;
        }
    }

    private async Task<WikiPageContent?> GetPageContentWithPathFallbackAsync(WikiConfiguration configuration, string path)
    {
        var primary = ResolveKnownPath(path) ?? NormalizePath(path);
        var content = await _wikiPageBrowserService.GetPageContentAsync(configuration, primary);
        if (content != null && !string.IsNullOrWhiteSpace(content.Content))
        {
            return content;
        }

        var fallback = primary.TrimStart('/');
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return content;
        }

        var fallbackKnown = ResolveKnownPath(fallback) ?? fallback;
        var fallbackContent = await _wikiPageBrowserService.GetPageContentAsync(configuration, fallbackKnown);
        return fallbackContent ?? content;
    }

    private void RegisterKnownPaths(IEnumerable<string> paths)
    {
        _knownPagePaths.Clear();
        foreach (var raw in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var normalized = NormalizePath(raw);
            _knownPagePaths[normalized] = raw;
            _knownPagePaths[normalized.TrimStart('/')] = raw;
        }
    }

    private string? ResolveKnownPath(string path)
    {
        var normalized = NormalizePath(path);
        if (_knownPagePaths.TryGetValue(normalized, out var exact))
        {
            return exact;
        }

        var trimmed = normalized.TrimStart('/');
        if (_knownPagePaths.TryGetValue(trimmed, out exact))
        {
            return exact;
        }

        return null;
    }

    private void RegisterKnownLastUpdated(IEnumerable<WikiPage> pages)
    {
        _pageLastUpdatedByPath.Clear();
        foreach (var page in pages.Where(p => !string.IsNullOrWhiteSpace(p.Path) && p.LastModified != default))
        {
            RegisterPathLastUpdated(page.Path, page.LastModified);
        }
    }

    private void RegisterPathLastUpdated(string path, DateTime lastModified)
    {
        var normalized = NormalizePath(path);
        _pageLastUpdatedByPath[normalized] = lastModified;
        _pageLastUpdatedByPath[normalized.TrimStart('/')] = lastModified;
    }

    private DateTime? TryResolveCurrentPageLastUpdated(RenderedWikiPage? renderedPage)
    {
        if (!string.IsNullOrWhiteSpace(renderedPage?.HtmlFilePath) && File.Exists(renderedPage.HtmlFilePath))
        {
            return File.GetLastWriteTime(renderedPage.HtmlFilePath);
        }

        if (_isLocalMarkdownMode && !string.IsNullOrWhiteSpace(_currentLocalMarkdownPath) && File.Exists(_currentLocalMarkdownPath))
        {
            return File.GetLastWriteTime(_currentLocalMarkdownPath);
        }

        var path = renderedPage?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = NormalizePath(path);
        if (_pageLastUpdatedByPath.TryGetValue(normalized, out var direct))
        {
            return direct;
        }

        var trimmed = normalized.TrimStart('/');
        if (_pageLastUpdatedByPath.TryGetValue(trimmed, out direct))
        {
            return direct;
        }

        return null;
    }

    public void UpsertAdditionalPage(string title, string markdownContent)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? AppText.S("wpf.workspace.additional_page.default_title", "AI Generated Page")
            : title.Trim();

        var htmlFragment = RenderMarkdownPreviewFragment(markdownContent ?? string.Empty, ResolveCurrentMarkdownDialect());
        var existing = AdditionalExportPages.FirstOrDefault(p => string.Equals(p.Title, normalizedTitle, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            AdditionalExportPages.Add(new AdditionalExportPage
            {
                Title = normalizedTitle,
                Markdown = markdownContent ?? string.Empty,
                HtmlFragment = htmlFragment
            });
        }
        else
        {
            existing.Markdown = markdownContent ?? string.Empty;
            existing.HtmlFragment = htmlFragment;
        }

        AdditionalPagesCount = AdditionalExportPages.Count;
        OnPropertyChanged(nameof(AdditionalExportPages));
    }

    public void ClearAdditionalPages()
    {
        AdditionalExportPages.Clear();
        AdditionalPagesCount = 0;
        OnPropertyChanged(nameof(AdditionalExportPages));
    }

    public void ClearLoadedState()
    {
        PageTree.Clear();
        _knownPagePaths.Clear();
        _pageLastUpdatedByPath.Clear();
        _generatedPageMarkdown.Clear();
        _renderedPages.Clear();
        _hasEffectiveAdminAccess = false;
        _currentRenderedPageIndex = -1;
        SelectedPageNode = null;
        CurrentPageMarkdown = string.Empty;
        CurrentPageHtml = string.Empty;
        CurrentDocumentTitle = string.Empty;
        _currentPageLastUpdated = null;
        OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
        PageNavigationText = string.Empty;
        Status = AppText.S("wpf.workspace.status.ready", "Ready");
        OnPropertyChanged(nameof(CanNavigatePrevious));
        OnPropertyChanged(nameof(CanNavigateNext));
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public string CurrentDocumentTitleOrFallback()
    {
        if (!string.IsNullOrWhiteSpace(CurrentDocumentTitle))
        {
            return CurrentDocumentTitle;
        }

        return AppText.S("wpf.workspace.current_source.untitled", "Current page");
    }

    public string GetCurrentAiSourceContent()
    {
        if (!string.IsNullOrWhiteSpace(CurrentPageMarkdown))
        {
            return CurrentPageMarkdown;
        }

        return ExtractTextFromHtml(CurrentPageHtml);
    }

    public async Task<IReadOnlyList<AiSourceItem>> GetAllLoadedAiSourceContentAsync()
    {
        var items = new List<AiSourceItem>();
        if (_renderedPages.Count == 0)
        {
            return items;
        }

        foreach (var page in _renderedPages)
        {
            var title = string.IsNullOrWhiteSpace(page.Path) ? AppText.S("wpf.workspace.current_source.untitled", "Page") : page.Path;
            var content = string.Empty;

            if (_generatedPageMarkdown.TryGetValue(page.Path ?? string.Empty, out var generatedMarkdown))
            {
                content = generatedMarkdown;
            }
            else if (_isLocalMarkdownMode && !string.IsNullOrWhiteSpace(_currentLocalMarkdownPath) && File.Exists(_currentLocalMarkdownPath))
            {
                content = await File.ReadAllTextAsync(_currentLocalMarkdownPath);
            }
            else if (SelectedWiki != null && !string.IsNullOrWhiteSpace(page.Path))
            {
                var wikiContent = await GetPageContentWithPathFallbackAsync(SelectedWiki, page.Path);
                content = wikiContent?.Content ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(content) &&
                !string.IsNullOrWhiteSpace(page.HtmlFilePath) &&
                File.Exists(page.HtmlFilePath))
            {
                var html = await SecureCacheFile.ReadTextAsync(page.HtmlFilePath);
                content = ExtractTextFromHtml(html);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            items.Add(new AiSourceItem(title, content));
        }

        return items;
    }


    private static IEnumerable<string> FlattenCheckedPaths(IEnumerable<WikiPageNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsChecked && node.IsPage && !string.IsNullOrWhiteSpace(node.Path))
            {
                yield return node.Path;
            }

            foreach (var child in FlattenCheckedPaths(node.Children))
            {
                yield return child;
            }
        }
    }

    private static void CheckAll(IEnumerable<WikiPageNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsChecked = true;
        }
    }

    private async Task<IReadOnlyList<WikiConfiguration>> ApplyWikiAccessFilterAsync(IEnumerable<WikiConfiguration> configurations)
    {
        var wikiList = configurations.ToList();
        if (IsCurrentUserAdmin())
        {
            return wikiList;
        }

        var currentUserId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return [];
        }

        var policies = await _adminCatalogService.LoadAccessPoliciesAsync();
        var identityTokens = GetCurrentIdentityTokens();
        var applicable = policies.Where(p =>
            p.IsActive &&
            p.IdentityType == AccessPolicyIdentityType.User &&
            identityTokens.Contains((p.IdentityId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase));

        var permittedWikiIds = applicable
            .SelectMany(p => p.Wikis)
            .Where(r => r.CanView)
            .Select(r => r.WikiId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return wikiList.Where(w => permittedWikiIds.Contains(w.Id)).ToList();
    }

    private async Task<List<string>> GetEffectiveStartPointsAsync()
    {
        var configured = GetConfiguredStartPoints();
        if (IsCurrentUserAdmin())
        {
            return configured;
        }

        var userAccess = await GetCurrentUserWikiAccessAsync();
        var userPoints = string.IsNullOrWhiteSpace(userAccess.StartPoints)
            ? []
            : userAccess.StartPoints
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

        if (configured.Count == 0)
        {
            return ReduceToTopLevelStartPoints(userPoints);
        }

        if (userPoints.Count == 0)
        {
            return configured;
        }

        var merged = new List<string>();
        foreach (var c in configured)
        {
            foreach (var u in userPoints)
            {
                if (string.Equals(c, u, StringComparison.OrdinalIgnoreCase) ||
                    c.StartsWith(u + "/", StringComparison.OrdinalIgnoreCase))
                {
                    merged.Add(c);
                    continue;
                }

                if (u.StartsWith(c + "/", StringComparison.OrdinalIgnoreCase))
                {
                    merged.Add(u);
                }
            }
        }

        return ReduceToTopLevelStartPoints(merged);
    }

    private List<string> GetConfiguredStartPoints()
    {
        if (SelectedWiki == null || string.IsNullOrWhiteSpace(SelectedWiki.RootPath))
        {
            return [];
        }

        var points = SelectedWiki.RootPath
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ReduceToTopLevelStartPoints(points);
    }

    private async Task<WikiAccessRule> GetCurrentUserWikiAccessAsync()
    {
        if (SelectedWiki == null || IsCurrentUserAdmin())
        {
            return new WikiAccessRule { CanView = true };
        }

        var policies = await _adminCatalogService.LoadAccessPoliciesAsync();
        var identityTokens = GetCurrentIdentityTokens();
        var applicable = policies.Where(p =>
            p.IsActive &&
            p.IdentityType == AccessPolicyIdentityType.User &&
            identityTokens.Contains((p.IdentityId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase));

        var rules = applicable
            .SelectMany(p => p.Wikis)
            .Where(r => string.Equals(r.WikiId, SelectedWiki.Id, StringComparison.OrdinalIgnoreCase) && r.CanView)
            .ToList();

        if (rules.Count == 0)
        {
            return new WikiAccessRule();
        }

        return new WikiAccessRule
        {
            WikiId = SelectedWiki.Id,
            CanView = true,
            StartPoints = string.Join("|",
                rules.SelectMany(r => ParseStartPoints(r.StartPoints)).Distinct(StringComparer.OrdinalIgnoreCase))
        };
    }

    private static List<string> FilterPathsByStartPoints(IEnumerable<string> paths, IReadOnlyCollection<string> startPoints)
    {
        var normalizedPaths = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (startPoints == null || startPoints.Count == 0)
        {
            return normalizedPaths;
        }

        var normalizedStartPoints = startPoints
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalizedPaths
            .Where(candidate => normalizedStartPoints.Any(sp =>
                sp == "/" ||
                string.Equals(candidate, sp, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(sp + "/", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static List<string> ParseStartPoints(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return [];
        }

        return rootPath.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ReduceToTopLevelStartPoints(IEnumerable<string> startPoints)
    {
        var ordered = startPoints
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<string>();
        foreach (var candidate in ordered)
        {
            var isDescendant = result.Any(existing =>
                candidate.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase));
            if (!isDescendant)
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private bool IsCurrentUserAdmin()
    {
        return _hasEffectiveAdminAccess;
    }

    private string GetCurrentUserId()
    {
        return _authenticationService.CurrentUser?.Id ?? string.Empty;
    }

    private HashSet<string> GetCurrentIdentityTokens()
    {
        var user = _authenticationService.CurrentUser;
        var values = new[]
        {
            user?.Id,
            user?.Username,
            user?.Email,
            user?.ProviderId
        };

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> ResolveEffectiveAdminAsync()
    {
        var currentUser = _authenticationService.CurrentUser;
        if (currentUser == null)
        {
            return false;
        }

        try
        {
            var identityTokens = GetCurrentIdentityTokens();
            if (identityTokens.Count == 0)
            {
                return false;
            }

            var policies = await _adminCatalogService.LoadAccessPoliciesAsync();
            return policies.Any(p =>
                p.IsActive &&
                p.IsAdmin &&
                p.IdentityType == AccessPolicyIdentityType.User &&
                identityTokens.Contains((p.IdentityId ?? string.Empty).Trim()));
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        var value = path.Trim();
        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        if (value != "/")
        {
            value = value.TrimEnd('/');
        }

        return value;
    }

    private static List<WikiPageNodeViewModel> BuildTree(IEnumerable<string> pagePaths, IReadOnlyList<string> startPoints)
    {
        if (startPoints.Any(sp => !string.Equals(sp, "/", StringComparison.Ordinal)))
        {
            return BuildTreeAnchoredAtStartPoints(pagePaths, startPoints);
        }

        return BuildTreeFromAbsolutePaths(pagePaths);
    }

    private static List<WikiPageNodeViewModel> BuildTreeAnchoredAtStartPoints(IEnumerable<string> pagePaths, IReadOnlyList<string> startPoints)
    {
        var normalizedPaths = pagePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var realPathSet = normalizedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roots = new Dictionary<string, WikiPageNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var startPoint in startPoints)
        {
            if (startPoint == "/")
            {
                continue;
            }

            var title = startPoint.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            roots[startPoint] = new WikiPageNodeViewModel
            {
                Title = title,
                Path = startPoint,
                IsPage = realPathSet.Contains(startPoint),
                IsChecked = false
            };
        }

        foreach (var path in normalizedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var matchingStartPoint = startPoints
                .OrderByDescending(x => x.Length)
                .FirstOrDefault(sp =>
                    sp == "/" ||
                    string.Equals(path, sp, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(sp + "/", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(matchingStartPoint))
            {
                continue;
            }

            if (matchingStartPoint == "/")
            {
                continue;
            }

            if (!roots.TryGetValue(matchingStartPoint, out var rootNode))
            {
                continue;
            }

            if (string.Equals(path, matchingStartPoint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = path.Substring(matchingStartPoint.Length).Trim('/');
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = matchingStartPoint;
            var parent = rootNode;

            foreach (var segment in segments)
            {
                currentPath += "/" + segment;
                var child = parent.Children.FirstOrDefault(c => string.Equals(c.Path, currentPath, StringComparison.OrdinalIgnoreCase));
                if (child == null)
                {
                    child = new WikiPageNodeViewModel
                    {
                        Title = segment,
                        Path = currentPath,
                        IsPage = realPathSet.Contains(currentPath),
                        Parent = parent,
                        IsChecked = false
                    };
                    parent.Children.Add(child);
                }
                else
                {
                    child.IsPage = child.IsPage || realPathSet.Contains(currentPath);
                }

                parent = child;
            }
        }

        return roots.Values.OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<WikiPageNodeViewModel> BuildTreeFromAbsolutePaths(IEnumerable<string> pagePaths)
    {
        var normalizedPaths = pagePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var realPathSet = normalizedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = new Dictionary<string, WikiPageNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var allNodes = new Dictionary<string, WikiPageNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in normalizedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var currentPath = string.Empty;
            WikiPageNodeViewModel? parent = null;

            foreach (var segment in segments)
            {
                currentPath += "/" + segment;
                if (!allNodes.TryGetValue(currentPath, out var node))
                {
                    node = new WikiPageNodeViewModel
                    {
                        Title = segment,
                        Path = currentPath,
                        IsPage = realPathSet.Contains(currentPath),
                        Parent = parent,
                        IsChecked = false
                    };
                    allNodes[currentPath] = node;

                    if (parent == null)
                    {
                        roots[currentPath] = node;
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }
                else
                {
                    node.IsPage = node.IsPage || realPathSet.Contains(currentPath);
                }

                parent = node;
            }
        }

        return roots.Values.OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public enum ExportScope
{
    CurrentDocument = 0,
    AllLoadedPages = 1
}

public sealed class AdditionalExportPage
{
    public string Title { get; set; } = string.Empty;
    public string Markdown { get; set; } = string.Empty;
    public string HtmlFragment { get; set; } = string.Empty;
}

public sealed class CodeThemeOption
{
    public string ThemeName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
