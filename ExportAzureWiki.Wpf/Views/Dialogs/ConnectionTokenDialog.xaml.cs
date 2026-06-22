using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Data;
using ExportAzureWiki.Platform.Setup;
using Microsoft.Win32;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class ConnectionTokenDialog : Window, INotifyPropertyChanged
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ConnectionBootstrapTokenService _tokenService;
    private string _expirationHours = "72";
    private string _tokenValue = string.Empty;
    private string _statusMessage = string.Empty;

    public ConnectionTokenDialog(IDbConnectionFactory connectionFactory, ConnectionBootstrapTokenService tokenService)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle => AppText.S("main.token.title", "Connection Token");
    public string HeaderText => AppText.S("wpf.token.header", "Generate Connection Token");
    public string DescriptionText => AppText.S("wpf.token.description", "Uses the current database configuration and creates a bootstrap token for new installations.");
    public string ExpirationHoursText => AppText.S("wpf.token.expiration_hours", "Expiration (hours)");
    public string GenerateText => AppText.S("wpf.token.generate", "Generate");
    public string TokenText => AppText.S("wpf.token.value", "Token");
    public string CloseText => AppText.S("common.close", "Close");

    public string ExpirationHours
    {
        get => _expirationHours;
        set
        {
            if (_expirationHours == value)
            {
                return;
            }

            _expirationHours = value;
            OnPropertyChanged();
        }
    }

    public string TokenValue
    {
        get => _tokenValue;
        private set
        {
            if (_tokenValue == value)
            {
                return;
            }

            _tokenValue = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    private void BtnGenerate_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ExpirationHours, out var hours) || hours <= 0 || hours > 8760)
        {
            StatusMessage = AppText.S("wpf.token.invalid_expiration", "Enter a valid expiration in hours (1-8760).");
            return;
        }

        var configuration = _connectionFactory.LoadConfiguration();
        if (configuration == null)
        {
            StatusMessage = AppText.S("main.token.error.no_db_config", "No database configuration is available to generate the token.");
            return;
        }

        try
        {
            var token = _tokenService.CreateToken(configuration, DateTime.UtcNow.AddHours(hours));
            TokenValue = token;

            var saveDialog = new SaveFileDialog
            {
                Filter = AppText.S("wpf.token.file.filter", "AWiki Token (*.awikitoken)|*.awikitoken|Text File (*.txt)|*.txt|All Files (*.*)|*.*"),
                DefaultExt = ".awikitoken",
                AddExtension = true,
                FileName = $"awiki-connection-token-{DateTime.Now:yyyyMMdd-HHmmss}.awikitoken",
                OverwritePrompt = true
            };

            var confirmed = saveDialog.ShowDialog(this);
            if (confirmed != true)
            {
                StatusMessage = AppText.S("wpf.token.save.canceled", "Save canceled.");
                return;
            }

            File.WriteAllText(saveDialog.FileName, token);
            StatusMessage = string.Format(
                AppText.S("wpf.token.save.success", "Connection token generated and saved to: {0}"),
                saveDialog.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                AppText.S("main.token.error.generic", "Error generating token: {0}"),
                ex.Message);
        }
    }

    private void BtnClose_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
