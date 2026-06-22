using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Localization;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class OAuthProviderEditDialog : Window
{
    private readonly OAuthProviderEditModel _model;

    public OAuthProviderEditDialog(OAuthProvider source, bool isNew)
    {
        InitializeComponent();
        _model = new OAuthProviderEditModel(source, isNew);
        DataContext = _model;
    }

    public OAuthProvider Result => _model.ToOAuthProvider();

    private void BtnSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_model.DisplayName) || string.IsNullOrWhiteSpace(_model.ClientId))
        {
            MessageBox.Show(
                AppText.S("wpf.providers.status.validation_oauth", "Provider Name and Display Name are required."),
                AppText.S("common.validation", "Validation"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class OAuthProviderEditModel : INotifyPropertyChanged
{
    private readonly int _id;
    private readonly DateTime _createdAt;
    private readonly string _configurationJson;
    private string _selectedProviderType;
    private string _displayName;
    private string _clientId;
    private string _clientSecret;
    private string _tenantId;
    private string _redirectUri;
    private string _scopes;
    private bool _isEnabled;

    public OAuthProviderEditModel(OAuthProvider source, bool isNew)
    {
        _id = source.Id;
        _createdAt = source.CreatedAt;
        _configurationJson = source.ConfigurationJson ?? string.Empty;
        _selectedProviderType = string.IsNullOrWhiteSpace(source.ProviderName) ? "AzureAD" : source.ProviderName;
        _displayName = source.DisplayName;
        _clientId = source.ClientId;
        _clientSecret = source.ClientSecret ?? string.Empty;
        _tenantId = source.TenantId ?? string.Empty;
        _redirectUri = string.IsNullOrWhiteSpace(source.RedirectUri) ? "http://localhost" : source.RedirectUri;
        _scopes = source.Scopes ?? string.Empty;
        _isEnabled = source.IsEnabled || isNew;
        DialogTitle = isNew
            ? AppText.S("wpf.providers.oauth.dialog.new.title", "New OAuth Provider")
            : AppText.S("wpf.providers.oauth.dialog.edit.title", "Edit OAuth Provider");

        if (isNew)
        {
            ApplyDefaultsByProvider();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DialogTitle { get; }
    public string ProviderTypeText => AppText.S("wpf.providers.oauth.provider_type", "Provider type");
    public string DisplayNameText => AppText.S("common.name", "Name");
    public string ClientIdText => AppText.S("wpf.providers.oauth.client_id", "Client ID");
    public string ClientSecretText => AppText.S("wpf.providers.oauth.client_secret", "Client Secret");
    public string TenantIdText => AppText.S("wpf.providers.oauth.tenant_id", "Tenant ID");
    public string RedirectUriText => AppText.S("wpf.providers.oauth.redirect_uri", "Redirect URI");
    public string ScopesText => AppText.S("wpf.providers.oauth.scopes", "Scopes");
    public string EnabledText => AppText.S("common.enabled", "Enabled");
    public string SaveText => AppText.S("common.save", "Save");
    public string CancelText => AppText.S("common.cancel", "Cancel");

    public IReadOnlyList<string> ProviderOptions { get; } = ["AzureAD", "Microsoft", "GitHub", "Google"];

    public string SelectedProviderType
    {
        get => _selectedProviderType;
        set
        {
            if (Set(ref _selectedProviderType, value))
            {
                ApplyDefaultsByProvider();
                OnPropertyChanged(nameof(TenantIdVisibility));
                OnPropertyChanged(nameof(ClientSecretVisibility));
                OnPropertyChanged(nameof(HelperText));
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    public string ClientId
    {
        get => _clientId;
        set => Set(ref _clientId, value);
    }

    public string ClientSecret
    {
        get => _clientSecret;
        set => Set(ref _clientSecret, value);
    }

    public string TenantId
    {
        get => _tenantId;
        set => Set(ref _tenantId, value);
    }

    public string RedirectUri
    {
        get => _redirectUri;
        set => Set(ref _redirectUri, value);
    }

    public string Scopes
    {
        get => _scopes;
        set => Set(ref _scopes, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    public Visibility TenantIdVisibility => SelectedProviderType.Equals("AzureAD", StringComparison.OrdinalIgnoreCase)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ClientSecretVisibility => SelectedProviderType.Equals("GitHub", StringComparison.OrdinalIgnoreCase) ||
                                                SelectedProviderType.Equals("Google", StringComparison.OrdinalIgnoreCase)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string HelperText => SelectedProviderType.ToLowerInvariant() switch
    {
        "azuread" => AppText.S("wpf.providers.oauth.help.azuread", "Azure AD requires Client ID, Tenant ID and Redirect URI."),
        "microsoft" => AppText.S("wpf.providers.oauth.help.microsoft", "Microsoft account requires Client ID and Redirect URI."),
        "github" => AppText.S("wpf.providers.oauth.help.github", "GitHub requires Client ID, Client Secret and Callback URL."),
        "google" => AppText.S("wpf.providers.oauth.help.google", "Google requires Client ID, Client Secret and Redirect URI."),
        _ => string.Empty
    };

    public OAuthProvider ToOAuthProvider()
    {
        var provider = (SelectedProviderType ?? string.Empty).Trim();
        var requiresClientSecret =
            provider.Equals("GitHub", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Google", StringComparison.OrdinalIgnoreCase);
        var requiresTenantId = provider.Equals("AzureAD", StringComparison.OrdinalIgnoreCase);

        return new OAuthProvider
        {
            Id = _id,
            ProviderName = provider,
            DisplayName = DisplayName?.Trim() ?? string.Empty,
            IsEnabled = IsEnabled,
            ClientId = ClientId?.Trim() ?? string.Empty,
            ClientSecret = requiresClientSecret ? ClientSecret?.Trim() : null,
            TenantId = requiresTenantId ? TenantId?.Trim() : null,
            RedirectUri = RedirectUri?.Trim(),
            Scopes = Scopes?.Trim(),
            ConfigurationJson = _configurationJson,
            CreatedAt = _createdAt == default ? DateTime.Now : _createdAt,
            LastModifiedAt = DateTime.Now
        };
    }

    private void ApplyDefaultsByProvider()
    {
        if (SelectedProviderType.Equals("AzureAD", StringComparison.OrdinalIgnoreCase) ||
            SelectedProviderType.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            ClientSecret = string.Empty;
        }

        if (!SelectedProviderType.Equals("AzureAD", StringComparison.OrdinalIgnoreCase))
        {
            TenantId = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = SelectedProviderType switch
            {
                "AzureAD" => AppText.S("wpf.providers.oauth.default_display.azuread", "Azure Active Directory"),
                "Microsoft" => AppText.S("wpf.providers.oauth.default_display.microsoft", "Microsoft Account"),
                _ => SelectedProviderType
            };
        }

        if (string.IsNullOrWhiteSpace(Scopes))
        {
            Scopes = SelectedProviderType switch
            {
                "AzureAD" => "openid profile email User.Read GroupMember.Read.All",
                "Microsoft" => "openid profile email User.Read",
                "GitHub" => "read:user user:email",
                "Google" => "openid profile email",
                _ => "openid profile email"
            };
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
