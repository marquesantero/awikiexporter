using System.IO;
using System.Windows;
using ExportAzureWiki.Localization;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class HelpViewerWindow : Window
{
    public HelpViewerWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
    }

    public string WindowTitle => AppText.S("wpf.help.window.title", "AWikiExport Help");
    public string HeaderText => AppText.S("wpf.help.header", "User Guide");
    public string CloseText => AppText.S("common.close", "Close");

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await wbHelp.EnsureCoreWebView2Async();
            wbHelp.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            wbHelp.CoreWebView2.Settings.AreDevToolsEnabled = false;
            wbHelp.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            wbHelp.CoreWebView2.Settings.IsStatusBarEnabled = false;

            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var languageSuffix = LocalizationManager.CurrentLanguage == SupportedLanguage.English ? "en" : "pt-BR";
            var helpFile = Path.Combine(basePath, "help", $"user-guide.{languageSuffix}.html");

            if (!File.Exists(helpFile))
            {
                helpFile = Path.Combine(basePath, "help", "user-guide.pt-BR.html");
            }

            if (File.Exists(helpFile))
            {
                wbHelp.Source = new Uri(helpFile);
                return;
            }

            wbHelp.NavigateToString($"<html><body><h2>{AppText.S("wpf.help.file_not_found", "Help file not found.")}</h2></body></html>");
        }
        catch
        {
            wbHelp.NavigateToString($"<html><body><h2>{AppText.S("wpf.help.load_error", "Error loading help.")}</h2></body></html>");
        }
    }

    private void BtnClose_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
