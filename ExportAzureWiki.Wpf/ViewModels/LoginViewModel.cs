using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Services;
using ExportAzureWiki.Wpf.Commands;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authService;
    private readonly IAdminCatalogService _adminCatalogService;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _isAzureLoginEnabled;
    private LanguageOption _selectedLanguage;

    public event EventHandler? LoginSucceeded;

    public LoginViewModel(IAuthenticationService authService, IAdminCatalogService adminCatalogService)
    {
        _authService = authService;
        _adminCatalogService = adminCatalogService;
        LoginCommand = new RelayCommand(async () => await LoginAsync(), () => !IsBusy);
        LoginAzureCommand = new RelayCommand(async () => await LoginAzureAsync(), () => !IsBusy && IsAzureLoginEnabled);
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
        _ = LoadAuthOptionsAsync();
    }

    public string BrandTitle => AppText.S("login.brand.title", "AWikiExport");
    public string BrandSubtitle => AppText.S("wpf.login.brand.subtitle", "Exporter");
    public string UsernameLabel => AppText.S("admin.user.field.username", "Username");
    public string PasswordLabel => AppText.S("common.password", "Password");
    public string SignInText => AppText.S("login.button.primary", "Sign in");
    public string SignInAzureText => AppText.S("login.button.azure", "Sign in with Azure");
    public string LanguageLabel => AppText.S("login.language.label", "Language");
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            (LoginCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LoginAzureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsAzureLoginEnabled
    {
        get => _isAzureLoginEnabled;
        private set
        {
            _isAzureLoginEnabled = value;
            OnPropertyChanged();
            (LoginAzureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public System.Windows.Input.ICommand LoginCommand { get; }
    public System.Windows.Input.ICommand LoginAzureCommand { get; }

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
        }
    }

    public async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var username = Username ?? string.Empty;
            var password = Password ?? string.Empty;
            Status = AppText.S("wpf.login.status.authenticating", "Authenticating...");
            LoggingService.LogInfo($"WPF_LOGIN local attempt user='{username.Trim()}', passwordLength={password.Length}");

            var result = await _authService.AuthenticateLocalAsync(username, password);
            if (!result.Success || result.User == null)
            {
                LoggingService.LogWarning($"WPF_LOGIN local failed user='{username.Trim()}', reason='{result.ErrorMessage}'");
                Status = result.ErrorMessage ?? AppText.S("wpf.login.status.invalid_credentials", "Invalid credentials.");
                return;
            }

            LoggingService.LogInfo($"WPF_LOGIN local success user='{result.User.Username}'");
            CompleteLogin(result.User);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.login.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoginAzureAsync()
    {
        if (IsBusy || !IsAzureLoginEnabled)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Status = AppText.S("wpf.login.status.authenticating", "Authenticating...");

            var result = await _authService.AuthenticateAzureAsync();
            if (!result.Success || result.User == null)
            {
                Status = result.ErrorMessage ?? AppText.S("wpf.login.status.invalid_credentials", "Invalid credentials.");
                return;
            }

            CompleteLogin(result.User);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.login.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CompleteLogin(AuthenticatedUser user)
    {
        Status = string.Format(
            AppText.S("wpf.login.status.success", "Welcome, {0}"),
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName);
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }

    public void ResetForm()
    {
        Username = string.Empty;
        Password = string.Empty;
        Status = string.Empty;
        _ = LoadAuthOptionsAsync();
    }

    private async Task LoadAuthOptionsAsync()
    {
        try
        {
            var cfg = await _adminCatalogService.LoadAuthConfigurationAsync();
            IsAzureLoginEnabled = cfg != null &&
                                  (cfg.AllowAzureAD || cfg.PrimaryMethod == Core.Models.AuthenticationMethod.AzureAD);
        }
        catch
        {
            // Keep login resilient; on failure, prefer disabling external entry until configuration is readable.
            IsAzureLoginEnabled = false;
        }
    }
}

