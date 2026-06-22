using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ExportAzureWiki.Data;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Models;
using ExportAzureWiki.Platform.Setup;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Data.Schema;

namespace ExportAzureWiki.Wpf.Views.Setup;

public partial class SetupWizardWindow : Window, INotifyPropertyChanged
{
    // Wizard steps. Existing-database installs skip Admin (Database -> Completion).
    private const int StepLanguage = 0;
    private const int StepWelcome = 1;
    private const int StepDatabase = 2;
    private const int StepAdmin = 3;
    private const int StepCompletion = 4;

    private enum SetupInstallMode { New = 0, Existing = 1 }
    private enum ExistingDbConfigMode { Manual = 0, Token = 1 }

    private readonly ISetupService _setupService;
    private readonly ConnectionBootstrapTokenService _tokenService = new();
    private readonly ConnectionBootstrapTokenStore _tokenStore = new();

    private int _currentStep;
    private SetupInstallMode _installMode = SetupInstallMode.New;
    private ExistingDbConfigMode _existingDbMode = ExistingDbConfigMode.Manual;
    private string _selectedDbType = "SQLite";
    private string _dbServer = "localhost";
    private string _dbDatabase = "AWikiExporterDB";
    private string _dbPort = "1433";
    private string _dbUser = string.Empty;
    private string _dbPassword = string.Empty;
    private bool _trustServerCertificate;
    private bool _useWindowsAuth;
    private string _dbFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExportAzureWiki", "data.db");
    private string _databaseStatus = string.Empty;
    private string _connectionToken = string.Empty;
    private string _adminUsername = "admin";
    private string _adminEmail = string.Empty;
    private string _adminPassword = string.Empty;
    private string _adminPasswordConfirm = string.Empty;
    private string _footerStatus = string.Empty;
    private bool _isBusy;
    private LanguageOption _selectedLanguage;

    public SetupWizardWindow(ISetupService setupService)
    {
        InitializeComponent();
        _setupService = setupService;
        DataContext = this;
        LanguageOptions =
        [
            new LanguageOption(SupportedLanguage.Portuguese, "Português"),
            new LanguageOption(SupportedLanguage.English, "English")
        ];
        _selectedLanguage = LanguageOptions.FirstOrDefault(x => x.Value == LocalizationManager.CurrentLanguage)
                            ?? LanguageOptions[0];

        if (_tokenStore.TryLoad(out var savedToken))
        {
            _connectionToken = savedToken;
        }

        PasswordRules =
        [
            new PolicyRule("setup.admin.policy.min_length", "At least 8 characters"),
            new PolicyRule("setup.admin.policy.uppercase", "One uppercase letter"),
            new PolicyRule("setup.admin.policy.lowercase", "One lowercase letter"),
            new PolicyRule("setup.admin.policy.digit", "One digit"),
            new PolicyRule("setup.admin.policy.symbol", "One symbol")
        ];
        UpdatePasswordRules();

        CurrentStep = StepLanguage;
        Loaded += (_, _) => UpdateStepState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle => AppText.S("setup.wizard.caption", "Initial Setup - AWikiExport");
    public string TitleText => AppText.S("setup.wizard.caption", "Initial Setup - AWikiExport");
    public string StepText => AppText.Sf("setup.wizard.title", GetDisplayStepNumber(), GetTotalSteps());

    public string LanguageTabText => AppText.S("setup.tab.language", "Language");
    public string LanguageStepTitle => AppText.S("setup.language.title", "Select your language");
    public string LanguageStepDescription => AppText.S("setup.language.description", "Choose the application language to continue the initial setup.");

    public string WelcomeTabText => AppText.S("setup.tab.welcome", "Welcome");
    public string WelcomeTitle => AppText.S("setup.welcome.title", "Welcome to AWikiExport");
    public string WelcomeDescription => AppText.S("setup.welcome.description", "This wizard will guide you through the initial setup.");
    public string WelcomeInfo => AppText.S("setup.welcome.info", "A new database will be created.");
    public string InstallModeText => AppText.S("setup.install_mode.label", "Installation mode");
    public string InstallModeNewText => AppText.S("setup.install_mode.new", "New installation (new database)");
    public string InstallModeExistingText => AppText.S("setup.install_mode.existing", "New app installation using existing database");

    public string DatabaseTabText => AppText.S("setup.tab.database", "Database");
    public string DatabaseTitle => AppText.S("setup.db.title", "Database Configuration");
    public string ExistingConnectionModeText => AppText.S("setup.existing.mode.label", "Existing database configuration");
    public string ExistingConnectionManualText => AppText.S("setup.existing.mode.manual", "Manual configuration");
    public string ExistingConnectionTokenText => AppText.S("setup.existing.mode.token", "Configuration token");
    public string TokenText => AppText.S("setup.existing.token", "Encrypted token:");
    public string ValidateTokenText => AppText.S("setup.existing.token.validate", "Validate token");
    public string DbTypeText => AppText.S("setup.db.type", "Database Type:");
    public string DbServerText => AppText.S("setup.db.server", "Server:");
    public string DbDatabaseText => AppText.S("setup.db.database", "Database:");
    public string DbPortText => AppText.S("setup.db.port", "Port:");
    public string DbUserText => AppText.S("setup.db.user", "Username:");
    public string DbPasswordText => AppText.S("setup.db.password", "Password:");
    public string DbFileText => AppText.S("setup.db.file", "File:");
    public string BrowseText => AppText.S("setup.db.browse", "Browse...");
    public string TestText => AppText.S("setup.db.test", "Test connection");
    public string WindowsAuthText => AppText.S("setup.db.windows_auth", "Use Windows Authentication");
    public string AdvancedOptionsText => AppText.S("setup.db.advanced", "Advanced options");
    public string TrustServerCertificateText => AppText.S("setup.db.trust_cert", "Trust the server certificate (TLS without validation)");
    public string TrustServerCertificateHint => AppText.S("setup.db.trust_cert.hint", "Use only on internal/test networks. Accepts certificates not trusted by this machine.");

    public string DbServerPlaceholder => AppText.S("setup.db.server.ph", "e.g. localhost");
    public string DbDatabasePlaceholder => AppText.S("setup.db.database.ph", "e.g. ExportAzureWiki");
    public string DbPortPlaceholder => AppText.S("setup.db.port.ph", "e.g. 1433");
    public string DbUserPlaceholder => AppText.S("setup.db.user.ph", "database user");
    public string DbFilePlaceholder => AppText.S("setup.db.file.ph", "path to the .db file");

    public string AdminTabText => AppText.S("setup.tab.admin", "Admin");
    public string AdminTitle => AppText.S("setup.admin.title", "Administrator User");
    public string AdminUsernameText => AppText.S("setup.admin.username", "Username:");
    public string AdminEmailText => AppText.S("setup.admin.email", "Email:");
    public string AdminPasswordText => AppText.S("setup.admin.password", "Password:");
    public string AdminPasswordConfirmText => AppText.S("setup.admin.confirm_password", "Confirm Password:");
    public string AdminUsernamePlaceholder => AppText.S("setup.admin.username.ph", "e.g. admin");
    public string AdminEmailPlaceholder => AppText.S("setup.admin.email.ph", "e.g. admin@company.com");
    public string PasswordRequirementsText => AppText.S("setup.admin.policy.title", "Password requirements:");
    public ObservableCollection<PolicyRule> PasswordRules { get; }

    public string CompletionTabText => AppText.S("setup.tab.completion", "Completion");
    public string CompletionTitle => AppText.S("setup.completion.title", "Setup Completed!");
    public string CompletionSummary => AppText.S("setup.completion.summary", "Settings applied successfully.");

    public string BackText => AppText.S("setup.nav.back", "< Back");
    public string NextText => CurrentStep == StepCompletion
        ? AppText.S("setup.nav.start_app", "Start Application")
        : AppText.S("setup.nav.next", "Next >");
    public string CancelText => AppText.S("common.cancel", "Cancel");

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            _currentStep = Math.Clamp(value, StepLanguage, StepCompletion);
            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(NextText));
            OnPropertyChanged(nameof(StepText));
            OnPropertyChanged(nameof(CanBack));
            OnPropertyChanged(nameof(CanNext));
        }
    }

    public bool CanBack => !_isBusy && CurrentStep > StepLanguage;
    public bool CanNext => !_isBusy;

    public bool IsNewInstallationMode
    {
        get => _installMode == SetupInstallMode.New;
        set
        {
            if (!value) { return; }
            _installMode = SetupInstallMode.New;
            OnInstallModeChanged();
        }
    }

    public bool IsExistingInstallationMode
    {
        get => _installMode == SetupInstallMode.Existing;
        set
        {
            if (!value) { return; }
            _installMode = SetupInstallMode.Existing;
            OnInstallModeChanged();
        }
    }

    public bool IsExistingManualMode
    {
        get => _existingDbMode == ExistingDbConfigMode.Manual;
        set
        {
            if (!value) { return; }
            _existingDbMode = ExistingDbConfigMode.Manual;
            OnExistingDbModeChanged();
        }
    }

    public bool IsExistingTokenMode
    {
        get => _existingDbMode == ExistingDbConfigMode.Token;
        set
        {
            if (!value) { return; }
            _existingDbMode = ExistingDbConfigMode.Token;
            OnExistingDbModeChanged();
        }
    }

    public bool ShowExistingConnectionModeOptions => IsExistingInstallationMode;
    public bool ShowManualConfigurationFields => !IsExistingInstallationMode || IsExistingManualMode;
    public bool ShowTokenConfigurationFields => IsExistingInstallationMode && IsExistingTokenMode;
    public bool ShowAdminStep => IsNewInstallationMode;

    public bool ShowServerFields => !string.Equals(_selectedDbType, "SQLite", StringComparison.OrdinalIgnoreCase);
    public bool ShowFileField => string.Equals(_selectedDbType, "SQLite", StringComparison.OrdinalIgnoreCase);
    public bool ShowWindowsAuthOption => string.Equals(_selectedDbType, "SqlServer", StringComparison.OrdinalIgnoreCase);
    public bool ShowSqlCredentials => ShowServerFields && !UseWindowsAuth;

    public bool TrustServerCertificate
    {
        get => _trustServerCertificate;
        set { _trustServerCertificate = value; OnPropertyChanged(nameof(TrustServerCertificate)); }
    }

    public bool UseWindowsAuth
    {
        get => _useWindowsAuth;
        set
        {
            _useWindowsAuth = value;
            OnPropertyChanged(nameof(UseWindowsAuth));
            OnPropertyChanged(nameof(ShowSqlCredentials));
        }
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value == null || ReferenceEquals(_selectedLanguage, value)) { return; }
            _selectedLanguage = value;
            OnPropertyChanged(nameof(SelectedLanguage));
            LocalizationManager.SetLanguage(value.Value);
            RefreshLocalizedTexts();
        }
    }

    public string SelectedDbType
    {
        get => _selectedDbType;
        set
        {
            _selectedDbType = value;
            // Prefill the standard port for the selected engine so the user
            // doesn't have to remember it (SQL Server 1433, PostgreSQL 5432,
            // MySQL 3306). SQLite is file-based and has no port.
            var defaultPort = DefaultPortFor(value);
            if (defaultPort != null)
            {
                DbPort = defaultPort;
            }
            OnPropertyChanged(nameof(SelectedDbType));
            OnPropertyChanged(nameof(ShowServerFields));
            OnPropertyChanged(nameof(ShowFileField));
            OnPropertyChanged(nameof(ShowWindowsAuthOption));
            OnPropertyChanged(nameof(ShowSqlCredentials));
        }
    }

    private static string? DefaultPortFor(string dbType) => dbType switch
    {
        "SqlServer" => "1433",
        "PostgreSQL" => "5432",
        "MySQL" => "3306",
        _ => null
    };

    public string DbServer { get => _dbServer; set { _dbServer = value; OnPropertyChanged(nameof(DbServer)); } }
    public string DbDatabase { get => _dbDatabase; set { _dbDatabase = value; OnPropertyChanged(nameof(DbDatabase)); } }
    public string DbPort { get => _dbPort; set { _dbPort = value; OnPropertyChanged(nameof(DbPort)); } }
    public string DbUser { get => _dbUser; set { _dbUser = value; OnPropertyChanged(nameof(DbUser)); } }
    public string DbPassword { get => _dbPassword; set { _dbPassword = value; OnPropertyChanged(nameof(DbPassword)); } }
    public string DbFilePath { get => _dbFilePath; set { _dbFilePath = value; OnPropertyChanged(nameof(DbFilePath)); } }
    public string ConnectionToken { get => _connectionToken; set { _connectionToken = value; OnPropertyChanged(nameof(ConnectionToken)); } }
    public string DatabaseStatus { get => _databaseStatus; set { _databaseStatus = value; OnPropertyChanged(nameof(DatabaseStatus)); } }
    public string AdminUsername { get => _adminUsername; set { _adminUsername = value; OnPropertyChanged(nameof(AdminUsername)); } }
    public string AdminEmail { get => _adminEmail; set { _adminEmail = value; OnPropertyChanged(nameof(AdminEmail)); } }
    public string AdminPassword { get => _adminPassword; set { _adminPassword = value; OnPropertyChanged(nameof(AdminPassword)); UpdatePasswordRules(); } }
    public string AdminPasswordConfirm { get => _adminPasswordConfirm; set { _adminPasswordConfirm = value; OnPropertyChanged(nameof(AdminPasswordConfirm)); } }
    public string FooterStatus { get => _footerStatus; set { _footerStatus = value; OnPropertyChanged(nameof(FooterStatus)); } }

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnInstallModeChanged()
    {
        OnPropertyChanged(nameof(IsNewInstallationMode));
        OnPropertyChanged(nameof(IsExistingInstallationMode));
        OnPropertyChanged(nameof(ShowExistingConnectionModeOptions));
        OnPropertyChanged(nameof(ShowManualConfigurationFields));
        OnPropertyChanged(nameof(ShowTokenConfigurationFields));
        OnPropertyChanged(nameof(ShowAdminStep));
        OnPropertyChanged(nameof(StepText));
        FooterStatus = IsNewInstallationMode
            ? AppText.S("setup.mode.new.info", "You will configure the database and create the first admin user.")
            : AppText.S("setup.mode.existing.info", "You will connect this installation to an existing database.");
    }

    private void OnExistingDbModeChanged()
    {
        OnPropertyChanged(nameof(IsExistingManualMode));
        OnPropertyChanged(nameof(IsExistingTokenMode));
        OnPropertyChanged(nameof(ShowManualConfigurationFields));
        OnPropertyChanged(nameof(ShowTokenConfigurationFields));
        if (IsExistingTokenMode)
        {
            FooterStatus = AppText.S("setup.existing.token.info", "Paste the encrypted token generated by an administrator.");
        }
    }

    private void RefreshLocalizedTexts()
    {
        foreach (var name in new[]
        {
            nameof(WindowTitle), nameof(TitleText), nameof(StepText),
            nameof(LanguageTabText), nameof(LanguageStepTitle), nameof(LanguageStepDescription),
            nameof(WelcomeTabText), nameof(WelcomeTitle), nameof(WelcomeDescription), nameof(WelcomeInfo),
            nameof(InstallModeText), nameof(InstallModeNewText), nameof(InstallModeExistingText),
            nameof(DatabaseTabText), nameof(DatabaseTitle), nameof(ExistingConnectionModeText),
            nameof(ExistingConnectionManualText), nameof(ExistingConnectionTokenText), nameof(TokenText),
            nameof(ValidateTokenText), nameof(DbTypeText), nameof(DbServerText), nameof(DbDatabaseText),
            nameof(DbPortText), nameof(DbUserText), nameof(DbPasswordText), nameof(DbFileText),
            nameof(BrowseText), nameof(TestText), nameof(WindowsAuthText), nameof(AdvancedOptionsText),
            nameof(TrustServerCertificateText), nameof(TrustServerCertificateHint),
            nameof(DbServerPlaceholder), nameof(DbDatabasePlaceholder),
            nameof(DbPortPlaceholder), nameof(DbUserPlaceholder), nameof(DbFilePlaceholder),
            nameof(AdminTabText), nameof(AdminTitle), nameof(AdminUsernameText), nameof(AdminEmailText),
            nameof(AdminPasswordText), nameof(AdminPasswordConfirmText), nameof(AdminUsernamePlaceholder),
            nameof(AdminEmailPlaceholder), nameof(CompletionTabText), nameof(CompletionTitle),
            nameof(CompletionSummary), nameof(BackText), nameof(NextText), nameof(CancelText),
            nameof(PasswordRequirementsText)
        })
        {
            OnPropertyChanged(name);
        }

        foreach (var rule in PasswordRules)
        {
            rule.RefreshText();
        }
    }

    private void UpdatePasswordRules()
    {
        if (PasswordRules == null)
        {
            return;
        }

        var password = _adminPassword ?? string.Empty;
        PasswordRules[0].IsSatisfied = password.Length >= 8;
        PasswordRules[1].IsSatisfied = password.Any(char.IsUpper);
        PasswordRules[2].IsSatisfied = password.Any(char.IsLower);
        PasswordRules[3].IsSatisfied = password.Any(char.IsDigit);
        PasswordRules[4].IsSatisfied = password.Any(c => !char.IsLetterOrDigit(c));
    }

    private void UpdateStepState() => tabSteps.SelectedIndex = CurrentStep;

    private int GetTotalSteps() => ShowAdminStep ? 5 : 4;

    private int GetDisplayStepNumber()
    {
        // Existing installs skip Admin, so Completion is the 4th visible step.
        if (!IsNewInstallationMode && CurrentStep == StepCompletion)
        {
            return 4;
        }

        return CurrentStep + 1;
    }

    private void BtnBack_OnClick(object sender, RoutedEventArgs e)
    {
        CurrentStep = (!IsNewInstallationMode && CurrentStep == StepCompletion)
            ? StepDatabase
            : CurrentStep - 1;
        UpdateStepState();
    }

    private async void BtnNext_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy) { return; }

        switch (CurrentStep)
        {
            case StepLanguage:
            case StepWelcome:
                CurrentStep++;
                UpdateStepState();
                return;

            case StepDatabase:
                var databaseOk = IsNewInstallationMode
                    ? await ValidateDatabaseStepAsync()
                    : IsExistingTokenMode
                        ? await ValidateExistingTokenStepAsync()
                        : await ValidateExistingManualStepAsync();
                if (!databaseOk) { return; }
                CurrentStep = IsNewInstallationMode ? StepAdmin : StepCompletion;
                UpdateStepState();
                return;

            case StepAdmin:
                if (!ValidateAdminStep()) { return; }
                CurrentStep = StepCompletion;
                UpdateStepState();
                return;

            default:
                await FinishSetupAsync();
                return;
        }
    }

    private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnBrowseDbFile_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = AppText.S("setup.db.sqlite.dialog_filter", "SQLite Database|*.db|All Files|*.*"),
            CheckFileExists = false
        };

        if (dlg.ShowDialog(this) == true)
        {
            DbFilePath = dlg.FileName;
        }
    }

    private async void BtnTestConnection_OnClick(object sender, RoutedEventArgs e)
    {
        var cfg = BuildDbConfiguration();
        var ok = await _setupService.TestConnectionAsync(cfg);
        DatabaseStatus = ok
            ? AppText.Sf("setup.db.status.connection_ok", cfg.Database)
            : AppText.S("setup.db.status.connection_error", "Connection failed.");
    }

    private async void BtnValidateToken_OnClick(object sender, RoutedEventArgs e)
        => await ValidateExistingTokenStepAsync();

    private async Task<bool> ValidateDatabaseStepAsync()
    {
        var cfg = BuildDbConfiguration();
        SetBusy(true);
        try
        {
            FooterStatus = AppText.S("setup.db.status.creating_schema", "Configuring database...");
            var ok = await _setupService.ConfigureDatabaseAsync(cfg, msg =>
            {
                Dispatcher.Invoke(() =>
                {
                    DatabaseStatus = msg;
                    FooterStatus = msg;
                });
            });

            if (!ok)
            {
                FooterStatus = AppText.S("setup.db.status.error", "Database setup failed.");
                return false;
            }

            FooterStatus = AppText.Sf("setup.db.status.created_configured_ok", cfg.Database);
            return true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> ValidateExistingManualStepAsync()
        => await ApplyExistingConfigurationAsync(BuildDbConfiguration(), persistToken: false);

    private async Task<bool> ValidateExistingTokenStepAsync()
    {
        if (!_tokenService.TryReadToken(ConnectionToken, out var config, out var error) || config == null)
        {
            DatabaseStatus = string.Format(AppText.S("setup.existing.token.invalid", "Invalid token: {0}"), error);
            FooterStatus = DatabaseStatus;
            return false;
        }

        return await ApplyExistingConfigurationAsync(config, persistToken: true);
    }

    private async Task<bool> ApplyExistingConfigurationAsync(DatabaseConfiguration config, bool persistToken)
    {
        SetBusy(true);
        try
        {
            FooterStatus = AppText.S("setup.existing.validating", "Validating existing database configuration...");
            _setupService.GetConnectionFactory().SetConnectionFromConfig(config);

            var canConnect = await _setupService.TestConnectionAsync(config);
            if (!canConnect)
            {
                DatabaseStatus = AppText.S("setup.existing.connection_error", "Connection failed for existing database.");
                FooterStatus = DatabaseStatus;
                return false;
            }

            var schemaManager = new SchemaManager(_setupService.GetConnectionFactory());
            await schemaManager.EnsureRequiredTablesAsync();

            var validSchema = await schemaManager.ValidateSchemaAsync();
            if (!validSchema)
            {
                DatabaseStatus = AppText.S("setup.existing.schema_invalid", "Existing database schema is not valid for AWiki. Use a prepared database.");
                FooterStatus = DatabaseStatus;
                return false;
            }

            if (persistToken && !string.IsNullOrWhiteSpace(ConnectionToken))
            {
                _tokenStore.Save(ConnectionToken);
            }

            DatabaseStatus = AppText.S("setup.existing.done", "Existing database configuration validated.");
            FooterStatus = DatabaseStatus;
            return true;
        }
        catch (Exception ex)
        {
            DatabaseStatus = string.Format(AppText.S("setup.existing.error", "Error: {0}"), ex.Message);
            FooterStatus = DatabaseStatus;
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool ValidateAdminStep()
    {
        if (string.IsNullOrWhiteSpace(AdminUsername))
        {
            FooterStatus = AppText.S("setup.admin.validation.username_required", "Username is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(AdminPassword))
        {
            FooterStatus = AppText.S("setup.admin.validation.password_required", "Password is required.");
            return false;
        }

        if (!string.Equals(AdminPassword, AdminPasswordConfirm, StringComparison.Ordinal))
        {
            FooterStatus = AppText.S("setup.admin.validation.password_mismatch", "Passwords do not match.");
            return false;
        }

        // Enforce the same policy CreateAdminUserAsync applies, here, so the user
        // gets the specific reason instead of a generic "could not create admin
        // user" only at the very end.
        var violation = ExportAzureWiki.Core.Authentication.PasswordPolicy.Default.FirstViolation(AdminPassword);
        if (violation != null)
        {
            FooterStatus = AppText.S(violation, "Password does not meet the security policy.");
            return false;
        }

        return true;
    }

    private async Task FinishSetupAsync()
    {
        SetBusy(true);
        try
        {
            FooterStatus = AppText.S("setup.completion.finishing", "Finishing setup...");

            if (IsNewInstallationMode)
            {
                // Run off the UI thread: the admin/authorization writes go through
                // synchronous (sync-over-async) data access. On the UI dispatcher
                // that deadlocks against a real-async provider like SQL Server
                // (it works with SQLite only because that completes synchronously).
                var adminCreated = await Task.Run(() =>
                    _setupService.CreateAdminUserAsync(AdminUsername, AdminEmail, AdminPassword));
                if (!adminCreated)
                {
                    FooterStatus = AppText.S("setup.admin.status.create_error", "Could not create admin user.");
                    return;
                }
            }

            var completed = await Task.Run(() => _setupService.CompleteSetupAsync());
            if (!completed)
            {
                FooterStatus = AppText.S("setup.completion.finish_error", "Could not complete the initial setup.");
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            FooterStatus = string.Format(AppText.S("setup.completion.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        OnPropertyChanged(nameof(CanBack));
        OnPropertyChanged(nameof(CanNext));
    }

    private DatabaseConfiguration BuildDbConfiguration()
    {
        var type = SelectedDbType switch
        {
            "SqlServer" => DatabaseType.SqlServer,
            "PostgreSQL" => DatabaseType.PostgreSQL,
            "MySQL" => DatabaseType.MySQL,
            _ => DatabaseType.SQLite
        };

        var config = new DatabaseConfiguration
        {
            DatabaseType = type,
            Server = DbServer?.Trim() ?? string.Empty,
            Database = DbDatabase?.Trim() ?? string.Empty,
            Username = DbUser?.Trim(),
            Password = DbPassword,
            FilePath = DbFilePath?.Trim(),
            UseWindowsAuth = UseWindowsAuth && type == DatabaseType.SqlServer,
            TrustServerCertificate = TrustServerCertificate
        };

        if (int.TryParse(DbPort, out var port))
        {
            config.Port = port;
        }

        if (type == DatabaseType.SQLite)
        {
            config.Database = Path.GetFileNameWithoutExtension(config.FilePath) ?? "AWikiExporterDB";
        }

        return config;
    }
}

/// <summary>
/// A single password-policy requirement shown in the live checklist. Exposes a
/// localized <see cref="Text"/> and an <see cref="IsSatisfied"/> flag the UI
/// binds to (check vs unchecked) as the user types.
/// </summary>
public sealed class PolicyRule : INotifyPropertyChanged
{
    private readonly string _key;
    private readonly string _fallback;
    private bool _isSatisfied;

    public PolicyRule(string key, string fallback)
    {
        _key = key;
        _fallback = fallback;
    }

    public string Text => AppText.S(_key, _fallback);

    public bool IsSatisfied
    {
        get => _isSatisfied;
        set
        {
            if (_isSatisfied != value)
            {
                _isSatisfied = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSatisfied)));
            }
        }
    }

    public void RefreshText() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
