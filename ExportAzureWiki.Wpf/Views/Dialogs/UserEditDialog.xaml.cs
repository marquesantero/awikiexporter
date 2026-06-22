using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class UserEditDialog : Window
{
    private readonly UserEditModel _model;

    public UserEditDialog(
        UserRecord source,
        bool isNew,
        Func<AuthenticationMethod, Task<IReadOnlyList<OAuthProvider>>> loadExternalProvidersAsync,
        Func<AuthenticationMethod, string?, int?, Task<IReadOnlyList<ExternalDirectoryUser>>> searchExternalUsersAsync)
    {
        InitializeComponent();
        _model = new UserEditModel(source, isNew, loadExternalProvidersAsync, searchExternalUsersAsync);
        DataContext = _model;
        Loaded += async (_, _) =>
        {
            await _model.InitializeAsync();
        };
    }

    public UserRecord Result => _model.ToUserRecord();
    public string? PlainPassword => _model.GetEffectivePassword();

    private async void BtnSearchExternal_OnClick(object sender, RoutedEventArgs e)
    {
        await _model.SearchExternalAsync();
    }

    private void BtnUseSelectedExternal_OnClick(object sender, RoutedEventArgs e)
    {
        _model.ApplySelectedExternalCandidate();
    }

    private void BtnSave_OnClick(object sender, RoutedEventArgs e)
    {
        var validationError = _model.Validate();
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            MessageBox.Show(
                validationError,
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

public sealed class UserEditModel : INotifyPropertyChanged
{
    private readonly int _id;
    private readonly DateTime _createdAt;
    private readonly DateTime? _lastLoginAt;
    private readonly DateTime? _lastModifiedAt;
    private readonly bool _isNew;
    private readonly Func<AuthenticationMethod, Task<IReadOnlyList<OAuthProvider>>> _loadExternalProvidersAsync;
    private readonly Func<AuthenticationMethod, string?, int?, Task<IReadOnlyList<ExternalDirectoryUser>>> _searchExternalUsersAsync;

    private string _username = string.Empty;
    private string _email = string.Empty;
    private string _displayName = string.Empty;
    private string _externalId = string.Empty;
    private bool _isActive = true;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _showPassword;
    private bool _isSearchingExternal;
    private string _searchTerm = string.Empty;
    private string _status = string.Empty;
    private ExternalDirectoryUser? _selectedExternalCandidate;
    private UserExternalProviderOption? _selectedExternalProvider;
    private UserAuthOption _selectedAuthOption;

    public UserEditModel(
        UserRecord source,
        bool isNew,
        Func<AuthenticationMethod, Task<IReadOnlyList<OAuthProvider>>> loadExternalProvidersAsync,
        Func<AuthenticationMethod, string?, int?, Task<IReadOnlyList<ExternalDirectoryUser>>> searchExternalUsersAsync)
    {
        _id = source.Id;
        _createdAt = source.CreatedAt;
        _lastLoginAt = source.LastLoginAt;
        _lastModifiedAt = source.LastModifiedAt;
        _isNew = isNew;
        _loadExternalProvidersAsync = loadExternalProvidersAsync;
        _searchExternalUsersAsync = searchExternalUsersAsync;

        _username = source.Username ?? string.Empty;
        _email = source.Email ?? string.Empty;
        _displayName = source.DisplayName ?? string.Empty;
        _externalId = source.ExternalId ?? string.Empty;
        _isActive = source.IsActive;

        AuthOptions =
        [
            new UserAuthOption(AuthenticationMethod.Local, AppText.S("admin.users_groups.auth.local", "Local")),
            new UserAuthOption(AuthenticationMethod.AzureAD, AppText.S("admin.users_groups.auth.azuread", "Azure AD")),
            new UserAuthOption(AuthenticationMethod.OAuth, AppText.S("wpf.users.auth.github", "GitHub")),
            new UserAuthOption(AuthenticationMethod.Windows, AppText.S("admin.users_groups.auth.windows", "Windows"))
        ];

        var currentMethod = source.AuthenticationMethod ?? AuthenticationMethod.Local;
        _selectedAuthOption = AuthOptions.FirstOrDefault(a => a.Method == currentMethod) ?? AuthOptions[0];

        DialogTitle = isNew
            ? AppText.S("wpf.users.dialog.new.title", "New User")
            : AppText.S("wpf.users.dialog.edit.title", "Edit User");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DialogTitle { get; }
    public string UsernameText => AppText.S("admin.user.field.username", "Username");
    public string EmailText => AppText.S("admin.user.field.email", "Email");
    public string DisplayNameText => AppText.S("admin.user.field.display_name", "Name");
    public string AuthModeText => AppText.S("wpf.users.auth", "Auth");
    public string PasswordText => AppText.S("admin.user.field.password", "Password");
    public string ConfirmPasswordText => AppText.S("admin.user.field.confirm_password", "Confirm Password");
    public string ActiveText => AppText.S("common.active", "Active");
    public string SaveText => AppText.S("common.save", "Save");
    public string CancelText => AppText.S("common.cancel", "Cancel");
    public string ProviderText => AppText.S("wpf.users.external.provider", "Provider");
    public string SearchText => AppText.S("admin.users_groups.provider_import.search", "Search:");
    public string SearchButtonText => IsSearchingExternal
        ? AppText.S("common.searching", "Searching...")
        : AppText.S("common.search", "Search");
    public string ExternalNameHeader => AppText.S("permissions.group.name", "Name");
    public string ExternalUsernameHeader => AppText.S("admin.user.field.username", "Username");
    public string ExternalEmailHeader => AppText.S("admin.user.field.email", "Email");
    public string UseSelectedText => AppText.S("wpf.users.external.use_selected", "Use selected");
    public string ImportedUserText => AppText.S("wpf.users.external.imported_user", "Imported user:");
    public string ImportedUserSummary => string.IsNullOrWhiteSpace(Username)
        ? AppText.S("wpf.users.external.none", "None")
        : $"{DisplayName} ({Username})";
    public string HelpText => IsLocalMode
        ? AppText.S("wpf.users.dialog.help.local", "Local user: set username, name, optional email, and password.")
        : AppText.S("wpf.users.dialog.help.external", "External user: search active users and apply selected.");

    public IReadOnlyList<UserAuthOption> AuthOptions { get; }
    public ObservableCollection<UserExternalProviderOption> ExternalProviders { get; } = [];
    public ObservableCollection<ExternalDirectoryUser> ExternalCandidates { get; } = [];

    public UserAuthOption SelectedAuthOption
    {
        get => _selectedAuthOption;
        set
        {
            if (Set(ref _selectedAuthOption, value))
            {
                OnPropertyChanged(nameof(IsLocalMode));
                OnPropertyChanged(nameof(LocalModeVisibility));
                OnPropertyChanged(nameof(ExternalModeVisibility));
                OnPropertyChanged(nameof(HelpText));
                ExternalCandidates.Clear();
                SelectedExternalCandidate = null;
                _ = ReloadExternalProvidersAsync();
                Status = string.Empty;
            }
        }
    }

    public bool IsLocalMode => SelectedAuthOption.Method == AuthenticationMethod.Local;
    public Visibility LocalModeVisibility => IsLocalMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExternalModeVisibility => IsLocalMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HiddenPasswordVisibility => ShowPassword ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VisiblePasswordVisibility => ShowPassword ? Visibility.Visible : Visibility.Collapsed;

    public string Username
    {
        get => _username;
        set
        {
            if (Set(ref _username, value))
            {
                OnPropertyChanged(nameof(ImportedUserSummary));
            }
        }
    }

    public string Email
    {
        get => _email;
        set => Set(ref _email, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (Set(ref _displayName, value))
            {
                OnPropertyChanged(nameof(ImportedUserSummary));
            }
        }
    }

    public string ExternalId
    {
        get => _externalId;
        set => Set(ref _externalId, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => Set(ref _confirmPassword, value);
    }

    public bool ShowPassword
    {
        get => _showPassword;
        set
        {
            if (Set(ref _showPassword, value))
            {
                OnPropertyChanged(nameof(HiddenPasswordVisibility));
                OnPropertyChanged(nameof(VisiblePasswordVisibility));
            }
        }
    }

    public bool IsSearchingExternal
    {
        get => _isSearchingExternal;
        private set
        {
            if (Set(ref _isSearchingExternal, value))
            {
                OnPropertyChanged(nameof(SearchButtonText));
            }
        }
    }

    public string SearchTerm
    {
        get => _searchTerm;
        set => Set(ref _searchTerm, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public ExternalDirectoryUser? SelectedExternalCandidate
    {
        get => _selectedExternalCandidate;
        set => Set(ref _selectedExternalCandidate, value);
    }

    public UserExternalProviderOption? SelectedExternalProvider
    {
        get => _selectedExternalProvider;
        set => Set(ref _selectedExternalProvider, value);
    }

    public async Task InitializeAsync()
    {
        await ReloadExternalProvidersAsync();
    }

    public async Task SearchExternalAsync()
    {
        if (IsLocalMode)
        {
            return;
        }

        if (SelectedExternalProvider == null)
        {
            Status = AppText.S("wpf.users.external.validation_provider_required", "Select a provider.");
            return;
        }

        try
        {
            IsSearchingExternal = true;
            Status = AppText.S("wpf.users.external.searching", "Searching users...");

            int? providerId = SelectedExternalProvider.Id > 0 ? SelectedExternalProvider.Id : null;
            var results = await _searchExternalUsersAsync(SelectedAuthOption.Method, SearchTerm, providerId);
            ExternalCandidates.Clear();
            foreach (var item in results.Where(r => r.IsActive))
            {
                ExternalCandidates.Add(item);
            }

            SelectedExternalCandidate = ExternalCandidates.FirstOrDefault();
            var baseStatus = string.Format(
                AppText.S("wpf.users.external.search_result", "{0} active user(s) found."),
                ExternalCandidates.Count);

            if (SelectedAuthOption.Method == AuthenticationMethod.Windows && ExternalCandidates.Count > 0)
            {
                var source = ExternalCandidates
                    .Select(c => c.ProviderName)
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "Unknown";
                Status = string.Format(
                    AppText.S("wpf.users.external.search_result_with_source", "{0} Source: {1}."),
                    baseStatus,
                    source);
            }
            else
            {
                Status = baseStatus;
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.users.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsSearchingExternal = false;
        }
    }

    public void ApplySelectedExternalCandidate()
    {
        if (SelectedExternalCandidate == null)
        {
            Status = AppText.S("wpf.users.external.select_one", "Select one user to import.");
            return;
        }

        Username = SelectedExternalCandidate.Username;
        DisplayName = string.IsNullOrWhiteSpace(SelectedExternalCandidate.DisplayName)
            ? SelectedExternalCandidate.Username
            : SelectedExternalCandidate.DisplayName;
        Email = SelectedExternalCandidate.Email ?? string.Empty;
        ExternalId = SelectedExternalCandidate.ExternalId;
        IsActive = true;
        Status = AppText.S("wpf.users.external.applied", "Selected external user applied.");
    }

    public string? Validate()
    {
        if (IsLocalMode)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                return AppText.S("admin.user.validation.username_required", "Username is required.");
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return AppText.S("wpf.users.validation.display_name_required", "Name is required.");
            }

            if (_isNew && string.IsNullOrWhiteSpace(Password))
            {
                return AppText.S("admin.user.validation.password_required_new", "Password is required for new users.");
            }

            if (!string.IsNullOrWhiteSpace(Password) || !string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
                {
                    return AppText.S("admin.user.validation.password_mismatch", "Passwords do not match.");
                }

                if ((Password ?? string.Empty).Length < 6)
                {
                    return AppText.S("admin.user.validation.password_min_length", "Password must be at least 6 characters.");
                }
            }
        }
        else
        {
            if (SelectedExternalProvider == null)
            {
                return AppText.S("wpf.users.external.validation_provider_required", "Select a provider.");
            }

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(ExternalId))
            {
                return AppText.S("wpf.users.external.validation_import_required", "Import one external active user.");
            }
        }

        return null;
    }

    public string? GetEffectivePassword()
    {
        if (!IsLocalMode)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(Password) ? null : Password;
    }

    public UserRecord ToUserRecord()
    {
        return new UserRecord
        {
            Id = _id,
            Username = Username?.Trim() ?? string.Empty,
            Email = Email?.Trim() ?? string.Empty,
            DisplayName = DisplayName?.Trim(),
            IsActive = IsActive,
            AuthenticationMethod = SelectedAuthOption.Method,
            ExternalId = string.IsNullOrWhiteSpace(ExternalId) ? null : ExternalId,
            CreatedAt = _createdAt == default ? DateTime.Now : _createdAt,
            LastLoginAt = _lastLoginAt,
            LastModifiedAt = _lastModifiedAt
        };
    }

    private async Task ReloadExternalProvidersAsync()
    {
        try
        {
            ExternalProviders.Clear();
            SelectedExternalProvider = null;

            if (IsLocalMode)
            {
                return;
            }

            var providers = await _loadExternalProvidersAsync(SelectedAuthOption.Method);
            foreach (var provider in providers.Where(p => p.IsEnabled))
            {
                ExternalProviders.Add(new UserExternalProviderOption(
                    provider.Id,
                    string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.ProviderName : provider.DisplayName,
                    provider.ProviderName));
            }

            if (SelectedAuthOption.Method == AuthenticationMethod.Windows)
            {
                ExternalProviders.Add(new UserExternalProviderOption(
                    0,
                    AppText.S("wpf.users.external.provider.windows_directory", "Windows Directory"),
                    "Windows"));
            }

            SelectedExternalProvider = ExternalProviders.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.users.status.error", "Error: {0}"), ex.Message);
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
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class UserAuthOption(AuthenticationMethod method, string label)
{
    public AuthenticationMethod Method { get; } = method;
    public string Label { get; } = label;
}

public sealed class UserExternalProviderOption(int id, string displayName, string providerName)
{
    public int Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public string ProviderName { get; } = providerName;
}
