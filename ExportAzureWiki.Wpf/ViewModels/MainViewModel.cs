using System.Windows.Input;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Wpf.Commands;
using System.Windows;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IAppServiceSet _services;
    private ViewModelBase _currentView;
    private string _currentSectionTitle;
    private bool _isAdminUser;
    private string _currentUserDisplay = "-";
    private bool _isWorkspaceSelected;
    private bool _isWikiManagementSelected;
    private bool _isUsersGroupsSelected;
    private bool _isPermissionsSelected;
    private bool _isProvidersSelected;
    private bool _isAiCenterSelected;
    private LanguageOption _selectedLanguage;
    private bool _isAuthenticatedUser;
    private bool _workspaceInitialized;
    private bool _workspaceNeedsWikiSync;
    private bool _workspaceNeedsAiProviderSync;
    public event EventHandler? HelpRequested;
    public event EventHandler? ConnectionTokenRequested;
    public event EventHandler? ManageDataRequested;

    public MainViewModel(IAppServiceSet services)
    {
        _services = services;

        Login = new LoginViewModel(_services.Authentication, _services.AdminCatalog);
        Workspace = new WorkspaceViewModel(
            _services.WikiCatalog,
            _services.WikiPageBrowser,
            _services.WikiPageRenderer,
            _services.AdminCatalog,
            _services.Authentication,
            _services.DocumentExport,
            _services.ExportHistory);
        WikiManagement = new WikiManagementViewModel(_services.WikiCatalog, _services.WikiPageBrowser);
        UsersAndGroups = new UsersGroupsViewModel(_services.AdminCatalog);
        Permissions = new PermissionsViewModel(_services.AdminCatalog, _services.WikiPageBrowser);
        Providers = new ProvidersViewModel(_services.AdminCatalog, _services.AiProviderProbe);
        AiCenter = new AiCenterViewModel(Workspace, _services.AiTextGeneration, _services.AdminCatalog);
        ExportCenter = new ExportCenterViewModel(Workspace, _services.DocumentExport);
        WikiManagement.WikisChanged += OnWikisCatalogChanged;
        Providers.AiProvidersChanged += OnAiProvidersChanged;

        _currentView = Login;
        _currentSectionTitle = AppText.S("wpf.section.login", "Login");
        _isAdminUser = false;
        LanguageOptions = new List<LanguageOption>
        {
            new(SupportedLanguage.Portuguese, "Português"),
            new(SupportedLanguage.English, "English")
        };
        _selectedLanguage = LanguageOptions.FirstOrDefault(x => x.Value == LocalizationManager.CurrentLanguage)
                            ?? LanguageOptions[0];
        LocalizationManager.LanguageChanged += (_, _) =>
        {
            var current = LanguageOptions.FirstOrDefault(x => x.Value == LocalizationManager.CurrentLanguage);
            if (current != null && !ReferenceEquals(_selectedLanguage, current))
            {
                _selectedLanguage = current;
                OnPropertyChanged(nameof(SelectedLanguage));
            }
        };
        Login.LoginSucceeded += async (_, _) =>
        {
            try
            {
                await RefreshAccessFlagsAsync();
                await AiCenter.RefreshProviderAvailabilityAsync();
                Workspace.SetAiProviderAvailability(AiCenter.HasConfiguredProvider);
            }
            catch
            {
                // Keep login flow resilient: post-login feature refresh failures
                // must not block navigation to workspace.
            }

            try
            {
                await EnterWorkspaceOrSetupWikiAsync();
            }
            catch
            {
                // Workspace can still open even if wiki loading fails temporarily.
                SetCurrent(Workspace, AppText.S("wpf.section.workspace", "Workspace"));
            }

            RaiseCommandStates();
        };

        ShowLoginCommand = new RelayCommand(() =>
        {
            SetCurrent(Login, AppText.S("wpf.section.login", "Login"));
            RaiseCommandStates();
        });
        ShowWorkspaceCommand = new RelayCommand(async () =>
        {
            SetCurrent(Workspace, AppText.S("wpf.section.workspace", "Workspace"));

            if (!_workspaceInitialized)
            {
                await Workspace.LoadWikisAsync();
                _workspaceInitialized = true;
                _workspaceNeedsWikiSync = false;
                _workspaceNeedsAiProviderSync = false;
                await AiCenter.RefreshProviderAvailabilityAsync();
                Workspace.SetAiProviderAvailability(AiCenter.HasConfiguredProvider);
                return;
            }

            if (_workspaceNeedsWikiSync)
            {
                await Workspace.SyncWikisCatalogAsync();
                _workspaceNeedsWikiSync = false;
            }

            if (_workspaceNeedsAiProviderSync)
            {
                await AiCenter.RefreshProviderAvailabilityAsync();
                Workspace.SetAiProviderAvailability(AiCenter.HasConfiguredProvider);
                _workspaceNeedsAiProviderSync = false;
            }
        }, () => IsAuthenticatedUser);
        ShowWikiManagementCommand = new RelayCommand(async () =>
        {
            if (!EnsureAdminAccess())
            {
                return;
            }

            SetCurrent(WikiManagement, AppText.S("wpf.section.wiki_management", "Wiki Management"));
            await WikiManagement.LoadAsync();
        }, () => IsAdminUser);
        ShowUsersGroupsCommand = new RelayCommand(async () =>
        {
            if (!EnsureAdminAccess())
            {
                return;
            }

            SetCurrent(UsersAndGroups, AppText.S("wpf.section.users_groups", "Users and Groups"));
            await UsersAndGroups.LoadAsync();
        }, () => IsAdminUser);
        ShowPermissionsCommand = new RelayCommand(async () =>
        {
            if (!EnsureAdminAccess())
            {
                return;
            }

            SetCurrent(Permissions, AppText.S("wpf.section.permissions", "Permissions"));
            await Permissions.LoadAsync();
        }, () => IsAdminUser);
        ShowProvidersCommand = new RelayCommand(async () =>
        {
            if (!EnsureAdminAccess())
            {
                return;
            }

            SetCurrent(Providers, AppText.S("wpf.section.providers", "Providers"));
            await Providers.LoadAsync();
        }, () => IsAdminUser);
        ShowConnectionTokenCommand = new RelayCommand(
            () =>
            {
                if (!EnsureAdminAccess())
                {
                    return;
                }

                ConnectionTokenRequested?.Invoke(this, EventArgs.Empty);
            },
            () => IsAdminUser);
        ShowAiCenterCommand = new RelayCommand(() => SetCurrent(AiCenter, AppText.S("wpf.section.ai_center", "AI Center")));
        ShowExportCenterCommand = new RelayCommand(() => SetCurrent(ExportCenter, AppText.S("wpf.section.export_center", "Export Center")));
        ShowHelpCommand = new RelayCommand(
            () => HelpRequested?.Invoke(this, EventArgs.Empty),
            () => IsAuthenticatedUser);
        LogoutCommand = new RelayCommand(() =>
        {
            _services.Authentication.SignOut();
            Workspace.ClearLoadedState();
            Workspace.SetAiProviderAvailability(false);
            Login.ResetForm();
            _workspaceInitialized = false;
            _workspaceNeedsWikiSync = false;
            _workspaceNeedsAiProviderSync = false;
            ResetAccessFlags();
            SetCurrent(Login, AppText.S("wpf.section.login", "Login"));
            RaiseCommandStates();
        }, () => IsAuthenticatedUser);

        // Opens the data management dialog. Available to any signed-in user;
        // the destructive/admin actions are gated inside the dialog.
        ManageDataCommand = new RelayCommand(
            () => ManageDataRequested?.Invoke(this, EventArgs.Empty),
            () => IsAuthenticatedUser);
    }

    /// <summary>Exposes the composed services so the shell can open dialogs
    /// (e.g. data management) with the same instances the app uses.</summary>
    public IAppServiceSet Services => _services;

    public LoginViewModel Login { get; }
    public WorkspaceViewModel Workspace { get; }
    public WikiManagementViewModel WikiManagement { get; }
    public UsersGroupsViewModel UsersAndGroups { get; }
    public PermissionsViewModel Permissions { get; }
    public ProvidersViewModel Providers { get; }
    public AiCenterViewModel AiCenter { get; }
    public ExportCenterViewModel ExportCenter { get; }

    public ViewModelBase CurrentView
    {
        get => _currentView;
        private set
        {
            _currentView = value;
            OnPropertyChanged();
        }
    }

    public string CurrentSectionTitle
    {
        get => _currentSectionTitle;
        private set
        {
            _currentSectionTitle = value;
            OnPropertyChanged();
        }
    }

    public bool IsWorkspaceSelected
    {
        get => _isWorkspaceSelected;
        private set
        {
            _isWorkspaceSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsWikiManagementSelected
    {
        get => _isWikiManagementSelected;
        private set
        {
            _isWikiManagementSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsUsersGroupsSelected
    {
        get => _isUsersGroupsSelected;
        private set
        {
            _isUsersGroupsSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsPermissionsSelected
    {
        get => _isPermissionsSelected;
        private set
        {
            _isPermissionsSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsProvidersSelected
    {
        get => _isProvidersSelected;
        private set
        {
            _isProvidersSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsAiCenterSelected
    {
        get => _isAiCenterSelected;
        private set
        {
            _isAiCenterSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsAdminUser
    {
        get => _isAdminUser;
        private set
        {
            _isAdminUser = value;
            OnPropertyChanged();
        }
    }

    public bool IsAuthenticatedUser
    {
        get => _isAuthenticatedUser;
        private set
        {
            _isAuthenticatedUser = value;
            OnPropertyChanged();
        }
    }

    public string CurrentUserDisplay
    {
        get => _currentUserDisplay;
        private set
        {
            _currentUserDisplay = value;
            OnPropertyChanged();
        }
    }

    public ICommand ShowLoginCommand { get; }
    public ICommand ShowWorkspaceCommand { get; }
    public ICommand ShowWikiManagementCommand { get; }
    public ICommand ShowUsersGroupsCommand { get; }
    public ICommand ShowPermissionsCommand { get; }
    public ICommand ShowProvidersCommand { get; }
    public ICommand ShowConnectionTokenCommand { get; }
    public ICommand ShowAiCenterCommand { get; }
    public ICommand ShowExportCenterCommand { get; }
    public ICommand ShowHelpCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand ManageDataCommand { get; }
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public string NavLogout => AppText.S("wpf.nav.logout", "Logout");
    public string NavWorkspace => AppText.S("wpf.nav.workspace", "Workspace");
    public string NavWikis => AppText.S("wpf.nav.wikis", "Wiki Management");
    public string NavUsers => AppText.S("wpf.nav.users_groups", "Users & Groups");
    public string NavPermissions => AppText.S("wpf.nav.permissions", "Permissions");
    public string NavProviders => AppText.S("wpf.nav.providers", "Providers");
    public string NavHelp => AppText.S("wpf.nav.help", "Help");
    public string NavConnectionToken => AppText.S("wpf.nav.connection_token", "Connection Token");
    public string NavManageData => AppText.S("wpf.data.menu", "Manage Data");
    public string NavAi => AppText.S("wpf.nav.ai", "AI Center");
    public string NavExport => AppText.S("wpf.nav.export", "Export Center");
    public string AppTitle => AppText.S("wpf.app.title", "AWikiExport");
    public string AppSubtitle => AppText.S("wpf.app.subtitle", "Modern Shell");
    public string WindowTitle => AppText.S("wpf.window.title", "AWikiExport");
    public string CurrentUserLabel => AppText.S("wpf.user.label", "User");
    public string LanguageLabel => AppText.S("login.language.label", "Language");

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value == null || ReferenceEquals(_selectedLanguage, value))
            {
                return;
            }

            _selectedLanguage = value;
            OnPropertyChanged();
            LocalizationManager.SetLanguage(value.Value);
            _ = PersistCurrentUserLanguageAsync(value.Value);
        }
    }

    public async Task InitializeAsync()
    {
        await RefreshAccessFlagsAsync();
        await AiCenter.RefreshProviderAvailabilityAsync();
        Workspace.SetAiProviderAvailability(AiCenter.HasConfiguredProvider);
        RaiseCommandStates();
        await Workspace.LoadWikisAsync();
        _workspaceInitialized = true;
        _workspaceNeedsWikiSync = false;
        _workspaceNeedsAiProviderSync = false;
    }

    /// <summary>
    /// Post-login landing. Loads the wiki catalog and, if nothing is configured
    /// yet, routes an admin straight to Wiki Management to add one instead of
    /// landing on an empty workspace. Non-admins stay on the workspace, which
    /// shows its empty state.
    /// </summary>
    private async Task EnterWorkspaceOrSetupWikiAsync()
    {
        SetCurrent(Workspace, AppText.S("wpf.section.workspace", "Workspace"));
        await Workspace.LoadWikisAsync();
        _workspaceInitialized = true;
        _workspaceNeedsWikiSync = false;

        if (Workspace.Wikis.Count == 0 && IsAdminUser)
        {
            SetCurrent(WikiManagement, AppText.S("wpf.section.wiki_management", "Wiki Management"));
            await WikiManagement.LoadAsync();
        }
    }

    private void OnWikisCatalogChanged(object? sender, EventArgs e)
    {
        _workspaceNeedsWikiSync = true;
    }

    private void OnAiProvidersChanged(object? sender, EventArgs e)
    {
        _workspaceNeedsAiProviderSync = true;
    }

    private void SetCurrent(ViewModelBase viewModel, string title)
    {
        CurrentView = viewModel;
        CurrentSectionTitle = title;
        UpdateSelectedMenuState(viewModel);
    }

    private bool EnsureAdminAccess()
    {
        if (IsAdminUser)
        {
            return true;
        }

        MessageBox.Show(
            AppText.S("wpf.access.denied.admin", "You do not have permission to access this administrative area."),
            AppText.S("wpf.access.denied.title", "Access denied"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private async Task RefreshAccessFlagsAsync()
    {
        var currentUser = _services.Authentication.CurrentUser;
        IsAuthenticatedUser = _services.Authentication.IsAuthenticated && currentUser != null;
        CurrentUserDisplay = currentUser?.DisplayName
            ?? currentUser?.Username
            ?? "-";
        ApplyUserPreferredLanguage(currentUser?.PreferredLanguage);
        IsAdminUser = await ResolveEffectiveAdminAsync(currentUser);
        RaiseCommandStates();
    }

    private void ResetAccessFlags()
    {
        IsAuthenticatedUser = false;
        CurrentUserDisplay = "-";
        IsAdminUser = false;
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        (ShowWorkspaceCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowWikiManagementCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowUsersGroupsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowPermissionsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowProvidersCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowConnectionTokenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ShowHelpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (LogoutCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ManageDataCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task<bool> ResolveEffectiveAdminAsync(ExportAzureWiki.Core.Authentication.AuthenticatedUser? currentUser)
    {
        if (currentUser == null)
        {
            return false;
        }

        try
        {
            var identityTokens = new[]
            {
                currentUser.Id,
                currentUser.Username,
                currentUser.Email,
                currentUser.ProviderId
            }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (identityTokens.Count == 0)
            {
                return false;
            }

            var policies = await _services.AdminCatalog.LoadAccessPoliciesAsync();
            return policies.Any(p =>
                p.IsActive &&
                p.IsAdmin &&
                p.IdentityType == ExportAzureWiki.Core.Models.AccessPolicyIdentityType.User &&
                identityTokens.Contains((p.IdentityId ?? string.Empty).Trim()));
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSelectedMenuState(ViewModelBase viewModel)
    {
        IsWorkspaceSelected = ReferenceEquals(viewModel, Workspace);
        IsWikiManagementSelected = ReferenceEquals(viewModel, WikiManagement);
        IsUsersGroupsSelected = ReferenceEquals(viewModel, UsersAndGroups);
        IsPermissionsSelected = ReferenceEquals(viewModel, Permissions);
        IsProvidersSelected = ReferenceEquals(viewModel, Providers);
        IsAiCenterSelected = ReferenceEquals(viewModel, AiCenter);
    }

    private async Task PersistCurrentUserLanguageAsync(SupportedLanguage language)
    {
        if (!_services.Authentication.IsAuthenticated)
        {
            return;
        }

        var code = language == SupportedLanguage.English ? "en" : "pt-BR";
        try
        {
            await _services.Authentication.SaveCurrentUserPreferredLanguageAsync(code);
        }
        catch
        {
            // Non-critical: language persistence must not break navigation.
        }
    }

    private void ApplyUserPreferredLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        var targetLanguage = ParseSupportedLanguage(languageCode);
        if (targetLanguage == null || targetLanguage == LocalizationManager.CurrentLanguage)
        {
            return;
        }

        var option = LanguageOptions.FirstOrDefault(x => x.Value == targetLanguage.Value);
        if (option == null)
        {
            return;
        }

        _selectedLanguage = option;
        OnPropertyChanged(nameof(SelectedLanguage));
        LocalizationManager.SetLanguage(option.Value);
    }

    private static SupportedLanguage? ParseSupportedLanguage(string languageCode)
    {
        var normalized = languageCode.Trim().ToLowerInvariant();
        if (normalized is "en" or "en-us" or "en-gb")
        {
            return SupportedLanguage.English;
        }

        if (normalized is "pt" or "pt-br")
        {
            return SupportedLanguage.Portuguese;
        }

        return null;
    }
}

