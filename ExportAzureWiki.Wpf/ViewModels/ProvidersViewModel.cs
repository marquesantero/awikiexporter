using System.Collections.ObjectModel;
using System.Windows;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Wpf.Commands;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class ProvidersViewModel : ViewModelBase
{
    private readonly IAdminCatalogService _service;

    /// <summary>Probe used by the AI provider edit dialog (model discovery + test).</summary>
    public IAiProviderProbeService AiProviderProbe { get; }

    private string _status = AppText.S("wpf.providers.status.ready", "Ready");
    private bool _isLoading;
    private OAuthProvider? _selectedOAuthProvider;
    private AiProvider? _selectedAiProvider;
    private AuthenticationMethod _primaryMethod = AuthenticationMethod.Local;
    private bool _allowLocalAuth = true;
    private bool _allowWindowsAuth;
    private bool _allowAzureAdAuth;
    private bool _syncWindowsGroups;
    private bool _syncAzureAdGroups;
    private string _azureAdTenantId = string.Empty;
    private bool _isApplyingAuthConfiguration;
    private bool _isSavingAuthConfiguration;
    private bool _hasPendingAuthConfigurationSave;

    public ObservableCollection<OAuthProvider> OAuthProviders { get; } = [];
    public ObservableCollection<AiProvider> AiProviders { get; } = [];
    public AuthenticationConfiguration? AuthenticationConfiguration { get; private set; }
    public System.Windows.Input.ICommand RefreshCommand { get; }
    public System.Windows.Input.ICommand DeleteOAuthCommand { get; }
    public System.Windows.Input.ICommand DeleteAiCommand { get; }
    public System.Windows.Input.ICommand SaveAuthConfigurationCommand { get; }
    public event EventHandler? AiProvidersChanged;

    public ProvidersViewModel(IAdminCatalogService service, IAiProviderProbeService aiProviderProbe)
    {
        _service = service;
        AiProviderProbe = aiProviderProbe;
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        DeleteOAuthCommand = new RelayCommand(async () => await DeleteOAuthAsync(), () => SelectedOAuthProvider is { Id: > 0 });
        DeleteAiCommand = new RelayCommand(async () => await DeleteAiAsync(), () => SelectedAiProvider is { Id: > 0 });
        SaveAuthConfigurationCommand = new RelayCommand(async () => await SaveAuthConfigurationAsync());
    }

    public string Title => AppText.S("wpf.providers.title", "Providers");
    public string RefreshText => AppText.S("common.refresh", "Refresh");
    public string SaveAuthText => AppText.S("common.save", "Save");
    public string OAuthProvidersText => AppText.S("wpf.providers.oauth.title", "OAuth Providers");
    public string AiProvidersText => AppText.S("wpf.providers.ai.title", "AI Providers");
    public string NewText => AppText.S("common.new", "New");
    public string EditText => AppText.S("common.edit", "Edit");
    public string DeleteText => AppText.S("common.delete", "Delete");
    public string EnabledText => AppText.S("common.enabled", "Enabled");
    public string DefaultText => AppText.S("common.default", "Default");
    public string ProviderHeader => AppText.S("wpf.providers.col.provider", "Provider");
    public string NameHeader => AppText.S("common.name", "Name");
    public string RedirectHeader => AppText.S("wpf.providers.col.redirect", "Redirect");
    public string DisplayHeader => AppText.S("wpf.providers.col.display", "Display");
    public string ModelHeader => AppText.S("wpf.providers.col.model", "Model");
    public string LocalAuthText => AppText.S("admin.users_groups.auth.local", "Local");
    public string WindowsAuthText => AppText.S("admin.users_groups.auth.windows", "Windows");
    public string AzureAdAuthText => AppText.S("admin.users_groups.auth.azuread", "Azure AD");
    public string OAuthAuthText => AppText.S("admin.users_groups.auth.oauth", "OAuth");
    public string MultipleAuthText => AppText.S("wpf.users.auth.multiple", "Multiple");
    public string PrimaryMethodGroupText => AppText.S("admin.auth_settings.primary_method", "Primary authentication method");
    public string AdditionalMethodsGroupText => AppText.S("admin.auth_settings.additional_methods", "Allowed Methods");
    public string AllowLocalAuthText => AppText.S("admin.auth_settings.allow.local", "Allow local login (username/password)");
    public string AllowWindowsAuthText => AppText.S("admin.auth_settings.allow.windows", "Allow Windows/AD login");
    public string AllowAzureAdAuthText => AppText.S("admin.auth_settings.allow.azure", "Allow Azure AD login");
    public string WindowsGroupText => AppText.S("admin.auth_settings.windows.group", "Windows/AD Settings");
    public string SyncWindowsGroupsText => AppText.S("admin.auth_settings.windows.sync", "Sync Windows/AD groups automatically");
    public string AzureAdGroupText => AppText.S("admin.auth_settings.azure.group", "Azure AD Settings");
    public string AzureTenantIdText => AppText.S("admin.auth_settings.azure.tenant_id", "Tenant ID:");
    public string AzureTenantIdPlaceholder => AppText.S("admin.auth_settings.azure.tenant_id.placeholder", "e.g.: 12345678-1234-1234-1234-123456789012");
    public string SyncAzureAdGroupsText => AppText.S("admin.auth_settings.azure.sync", "Sync Azure AD groups automatically");
    public bool AreAdditionalMethodsEditable => true;
    public bool IsWindowsSettingsVisible => PrimaryMethod == AuthenticationMethod.Windows || AllowWindowsAuth;
    public bool IsAzureSettingsVisible => PrimaryMethod == AuthenticationMethod.AzureAD || AllowAzureAdAuth;

    public IReadOnlyList<AuthMethodOption> PrimaryMethodOptions =>
    [
        new(AuthenticationMethod.Local, AppText.S("admin.auth_settings.primary.local", "Local System (Username and Password)")),
        new(AuthenticationMethod.Windows, AppText.S("admin.auth_settings.primary.windows", "Windows/Active Directory")),
        new(AuthenticationMethod.AzureAD, AppText.S("admin.auth_settings.primary.azure", "Azure Active Directory")),
        new(AuthenticationMethod.Multiple, AppText.S("admin.auth_settings.primary.multiple", "Multiple Methods"))
    ];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            OnPropertyChanged();
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

    public OAuthProvider? SelectedOAuthProvider
    {
        get => _selectedOAuthProvider;
        set
        {
            _selectedOAuthProvider = value;
            OnPropertyChanged();
            (DeleteOAuthCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public AiProvider? SelectedAiProvider
    {
        get => _selectedAiProvider;
        set
        {
            _selectedAiProvider = value;
            OnPropertyChanged();
            (DeleteAiCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public AuthenticationMethod PrimaryMethod
    {
        get => _primaryMethod;
        set
        {
            _primaryMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AreAdditionalMethodsEditable));
            OnPropertyChanged(nameof(IsWindowsSettingsVisible));
            OnPropertyChanged(nameof(IsAzureSettingsVisible));
            RequestAutoSaveAuthConfiguration();
        }
    }

    public bool AllowLocalAuth
    {
        get => _allowLocalAuth;
        set
        {
            _allowLocalAuth = value;
            OnPropertyChanged();
            RequestAutoSaveAuthConfiguration();
        }
    }

    public bool AllowWindowsAuth
    {
        get => _allowWindowsAuth;
        set
        {
            _allowWindowsAuth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWindowsSettingsVisible));
            RequestAutoSaveAuthConfiguration();
        }
    }

    public bool AllowAzureAdAuth
    {
        get => _allowAzureAdAuth;
        set
        {
            _allowAzureAdAuth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAzureSettingsVisible));
            RequestAutoSaveAuthConfiguration();
        }
    }

    public bool SyncWindowsGroups
    {
        get => _syncWindowsGroups;
        set
        {
            _syncWindowsGroups = value;
            OnPropertyChanged();
            RequestAutoSaveAuthConfiguration();
        }
    }

    public bool SyncAzureAdGroups
    {
        get => _syncAzureAdGroups;
        set { _syncAzureAdGroups = value; OnPropertyChanged(); }
    }

    public string AzureAdTenantId
    {
        get => _azureAdTenantId;
        set { _azureAdTenantId = value; OnPropertyChanged(); }
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            Status = AppText.S("wpf.providers.status.loading", "Loading providers...");

            OAuthProviders.Clear();
            AiProviders.Clear();

            var oauth = await _service.LoadOAuthProvidersAsync();
            var ai = await _service.LoadAiProvidersAsync();
            AuthenticationConfiguration = await _service.LoadAuthConfigurationAsync();
            LoadAuthSettingsFromConfiguration(AuthenticationConfiguration);

            foreach (var item in oauth) OAuthProviders.Add(item);
            foreach (var item in ai) AiProviders.Add(item);

            OnPropertyChanged(nameof(AuthenticationConfiguration));
            SelectedOAuthProvider = OAuthProviders.FirstOrDefault();
            SelectedAiProvider = AiProviders.FirstOrDefault();

            Status = string.Format(
                AppText.S("wpf.providers.status.loaded", "Loaded {0} OAuth and {1} AI provider(s)"),
                OAuthProviders.Count,
                AiProviders.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.providers.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public OAuthProvider CreateOAuthDraftForNew()
    {
        Status = AppText.S("wpf.providers.status.new_oauth", "Creating OAuth provider.");
        return new OAuthProvider
        {
            ProviderName = "AzureAD",
            DisplayName = AppText.S("wpf.providers.oauth.default_display.azuread", "Azure Active Directory"),
            IsEnabled = true,
            Scopes = "openid profile email"
        };
    }

    public OAuthProvider? CreateOAuthDraftFromSelected()
    {
        if (SelectedOAuthProvider == null)
        {
            return null;
        }

        var p = SelectedOAuthProvider;
        return new OAuthProvider
        {
            Id = p.Id,
            ProviderName = p.ProviderName,
            DisplayName = p.DisplayName,
            IsEnabled = p.IsEnabled,
            ClientId = p.ClientId,
            ClientSecret = p.ClientSecret,
            TenantId = p.TenantId,
            RedirectUri = p.RedirectUri,
            Scopes = p.Scopes,
            ConfigurationJson = p.ConfigurationJson,
            CreatedAt = p.CreatedAt,
            LastModifiedAt = p.LastModifiedAt
        };
    }

    public async Task<bool> SaveOAuthFromDialogAsync(OAuthProvider draft, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(draft.ProviderName) || string.IsNullOrWhiteSpace(draft.DisplayName))
        {
            Status = AppText.S("wpf.providers.status.validation_oauth", "Provider Name and Display Name are required.");
            return false;
        }

        if (!ValidateOAuthFields(draft, out var validationMessage))
        {
            Status = validationMessage;
            return false;
        }

        try
        {
            await _service.SaveOAuthProviderAsync(draft);
            await LoadAsync();
            if (isNew)
            {
                SelectedOAuthProvider = OAuthProviders.FirstOrDefault(x =>
                    x.Id == draft.Id ||
                    (string.Equals(x.ProviderName, draft.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(x.DisplayName, draft.DisplayName, StringComparison.OrdinalIgnoreCase)));
            }
            Status = AppText.S("wpf.providers.status.saved_oauth", "OAuth provider saved.");
            return true;
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.providers.status.error", "Error: {0}"), ex.Message);
            return false;
        }
    }

    private async Task DeleteOAuthAsync()
    {
        if (SelectedOAuthProvider == null || SelectedOAuthProvider.Id <= 0)
        {
            return;
        }

        var current = SelectedOAuthProvider;
        var confirmation = MessageBox.Show(
            string.Format(
                AppText.S("wpf.confirm.delete_oauth.message", "Delete OAuth provider '{0}'? This action cannot be undone."),
                current.DisplayName),
            AppText.S("wpf.confirm.delete_oauth.title", "Confirm Deletion"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            Status = AppText.S("wpf.confirm.delete_oauth.canceled", "OAuth provider deletion canceled.");
            return;
        }

        try
        {
            if (await _service.DeleteOAuthProviderAsync(current.Id))
            {
                OAuthProviders.Remove(current);
                SelectedOAuthProvider = OAuthProviders.FirstOrDefault();
                Status = AppText.S("wpf.providers.status.deleted_oauth", "OAuth provider deleted.");
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.providers.status.error", "Error: {0}"), ex.Message);
        }
    }

    public AiProvider CreateAiDraftForNew()
    {
        Status = AppText.S("wpf.providers.status.new_ai", "Creating AI provider.");
        return new AiProvider
        {
            ProviderName = "OpenAICompatible",
            DisplayName = AppText.S("wpf.providers.ai.default_display.new", "New AI Provider"),
            IsEnabled = true,
            ModelName = "gpt-4o-mini",
            ConfigurationJson = "{\"temperature\":0.2,\"max_tokens\":2000,\"top_p\":1.0,\"timeout_seconds\":120}"
        };
    }

    public AiProvider? CreateAiDraftFromSelected()
    {
        if (SelectedAiProvider == null)
        {
            return null;
        }

        var p = SelectedAiProvider;
        return new AiProvider
        {
            Id = p.Id,
            ProviderName = p.ProviderName,
            DisplayName = p.DisplayName,
            IsEnabled = p.IsEnabled,
            IsDefault = p.IsDefault,
            EndpointUrl = p.EndpointUrl,
            ApiKey = p.ApiKey,
            ModelName = p.ModelName,
            ApiVersion = p.ApiVersion,
            OrganizationId = p.OrganizationId,
            ConfigurationJson = p.ConfigurationJson,
            CreatedAt = p.CreatedAt,
            LastModifiedAt = p.LastModifiedAt
        };
    }

    public async Task<bool> SaveAiFromDialogAsync(AiProvider draft)
    {
        if (string.IsNullOrWhiteSpace(draft.ProviderName) || string.IsNullOrWhiteSpace(draft.DisplayName))
        {
            Status = AppText.S("wpf.providers.status.validation_ai", "Provider Name and Display Name are required.");
            return false;
        }

        try
        {
            await _service.SaveAiProviderAsync(draft);
            await LoadAsync();
            Status = AppText.S("wpf.providers.status.saved_ai", "AI provider saved.");
            AiProvidersChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.providers.status.error", "Error: {0}"), ex.Message);
            return false;
        }
    }

    private async Task DeleteAiAsync()
    {
        if (SelectedAiProvider == null || SelectedAiProvider.Id <= 0)
        {
            return;
        }

        var current = SelectedAiProvider;
        var confirmation = MessageBox.Show(
            string.Format(
                AppText.S("wpf.confirm.delete_ai.message", "Delete AI provider '{0}'? This action cannot be undone."),
                current.DisplayName),
            AppText.S("wpf.confirm.delete_ai.title", "Confirm Deletion"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            Status = AppText.S("wpf.confirm.delete_ai.canceled", "AI provider deletion canceled.");
            return;
        }

        try
        {
            if (await _service.DeleteAiProviderAsync(current.Id))
            {
                AiProviders.Remove(current);
                SelectedAiProvider = AiProviders.FirstOrDefault();
                Status = AppText.S("wpf.providers.status.deleted_ai", "AI provider deleted.");
                AiProvidersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.providers.status.error", "Error: {0}"), ex.Message);
        }
    }

    private async Task SaveAuthConfigurationAsync()
    {
        if (_isApplyingAuthConfiguration || _isSavingAuthConfiguration)
        {
            return;
        }

        try
        {
            _isSavingAuthConfiguration = true;
            if (!AllowLocalAuth && !AllowWindowsAuth && !AllowAzureAdAuth)
            {
                Status = AppText.S("wpf.providers.status.auth_method_required", "Select at least one authentication method.");
                return;
            }

            if (PrimaryMethod == AuthenticationMethod.Local && !AllowLocalAuth)
            {
                Status = AppText.S("wpf.providers.status.auth_primary_mismatch", "Primary authentication method must be enabled in allowed methods.");
                return;
            }

            if (PrimaryMethod == AuthenticationMethod.Windows && !AllowWindowsAuth)
            {
                Status = AppText.S("wpf.providers.status.auth_primary_mismatch", "Primary authentication method must be enabled in allowed methods.");
                return;
            }

            if (PrimaryMethod == AuthenticationMethod.AzureAD && !AllowAzureAdAuth)
            {
                Status = AppText.S("wpf.providers.status.auth_primary_mismatch", "Primary authentication method must be enabled in allowed methods.");
                return;
            }

            var previousConfig = AuthenticationConfiguration;
            var config = new AuthenticationConfiguration
            {
                Id = previousConfig?.Id ?? 0,
                PrimaryMethod = PrimaryMethod,
                AllowLocalAuth = AllowLocalAuth,
                AllowWindowsAuth = AllowWindowsAuth,
                AllowAzureAD = AllowAzureAdAuth,
                SyncWindowsGroups = SyncWindowsGroups,
                SyncAzureADGroups = false,
                AzureADTenantId = string.IsNullOrWhiteSpace(previousConfig?.AzureADTenantId) ? null : previousConfig.AzureADTenantId.Trim(),
                UseLocalPermissions = previousConfig?.UseLocalPermissions ?? true,
                UseWindowsPermissions = previousConfig?.UseWindowsPermissions ?? false,
                UseAzureADPermissions = previousConfig?.UseAzureADPermissions ?? false,
                RequireAuthentication = true,
                AutoCreateUsers = false,
                DefaultRole = string.IsNullOrWhiteSpace(previousConfig?.DefaultRole) ? "User" : previousConfig.DefaultRole.Trim(),
                CreatedAt = previousConfig?.CreatedAt ?? DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            AuthenticationConfiguration = config;
            OnPropertyChanged(nameof(AuthenticationConfiguration));

            var ok = await _service.SaveAuthenticationConfigurationAsync(config);
            Status = ok
                ? AppText.S("wpf.providers.status.saved_auth", "Authentication configuration saved.")
                : AppText.S("wpf.providers.status.save_auth_failed", "Could not save authentication configuration.");
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.providers.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            _isSavingAuthConfiguration = false;
            if (_hasPendingAuthConfigurationSave)
            {
                _hasPendingAuthConfigurationSave = false;
                await SaveAuthConfigurationAsync();
            }
        }
    }

    private void LoadAuthSettingsFromConfiguration(AuthenticationConfiguration? config)
    {
        _isApplyingAuthConfiguration = true;
        var cfg = config ?? new AuthenticationConfiguration();
        _primaryMethod = cfg.PrimaryMethod;
        _allowLocalAuth = cfg.AllowLocalAuth;
        _allowWindowsAuth = cfg.AllowWindowsAuth;
        _allowAzureAdAuth = cfg.AllowAzureAD;
        _syncWindowsGroups = cfg.SyncWindowsGroups;
        _syncAzureAdGroups = false;
        _azureAdTenantId = cfg.AzureADTenantId ?? string.Empty;

        OnPropertyChanged(nameof(PrimaryMethod));
        OnPropertyChanged(nameof(AllowLocalAuth));
        OnPropertyChanged(nameof(AllowWindowsAuth));
        OnPropertyChanged(nameof(AllowAzureAdAuth));
        OnPropertyChanged(nameof(SyncWindowsGroups));
        OnPropertyChanged(nameof(SyncAzureAdGroups));
        OnPropertyChanged(nameof(AzureAdTenantId));
        OnPropertyChanged(nameof(AreAdditionalMethodsEditable));
        OnPropertyChanged(nameof(IsWindowsSettingsVisible));
        OnPropertyChanged(nameof(IsAzureSettingsVisible));
        _isApplyingAuthConfiguration = false;
    }

    private void RequestAutoSaveAuthConfiguration()
    {
        if (_isApplyingAuthConfiguration)
        {
            return;
        }

        if (_isSavingAuthConfiguration)
        {
            _hasPendingAuthConfigurationSave = true;
            return;
        }

        _ = SaveAuthConfigurationAsync();
    }

    private bool ValidateOAuthFields(OAuthProvider provider, out string error)
    {
        error = string.Empty;
        var providerName = (provider.ProviderName ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider.ClientId))
        {
            error = AppText.S("wpf.providers.validation.client_id", "Client ID is required.");
            return false;
        }

        if (providerName is "azuread")
        {
            if (string.IsNullOrWhiteSpace(provider.TenantId))
            {
                error = AppText.S("wpf.providers.validation.tenant_id", "Tenant ID is required.");
                return false;
            }
        }
        else if (providerName is "github" or "google")
        {
            if (string.IsNullOrWhiteSpace(provider.ClientSecret))
            {
                error = AppText.S("wpf.providers.validation.client_secret", "Client Secret is required.");
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(provider.RedirectUri))
        {
            error = AppText.S("wpf.providers.validation.redirect_uri", "Redirect URI is required.");
            return false;
        }

        if (!Uri.TryCreate(provider.RedirectUri.Trim(), UriKind.Absolute, out _))
        {
            error = AppText.S("wpf.providers.validation.redirect_uri_invalid", "Redirect URI is invalid.");
            return false;
        }

        return true;
    }
}

public sealed class AuthMethodOption
{
    public AuthMethodOption(AuthenticationMethod value, string label)
    {
        Value = value;
        Label = label;
    }

    public AuthenticationMethod Value { get; }
    public string Label { get; }
}
