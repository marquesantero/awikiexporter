using System.Windows;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ExportAzureWiki.Wpf.Design;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Wpf.Views.Dialogs;
using ExportAzureWiki.Data;
using ExportAzureWiki.Platform.Setup;

namespace ExportAzureWiki.Wpf;

public partial class MainWindow : Window
{
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private readonly MainViewModel _viewModel;
    private bool _isNavCollapsed;

    public MainWindow()
        : this(new MainViewModel(new DesignAppServiceSet()))
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.HelpRequested += OnHelpRequested;
        _viewModel.ConnectionTokenRequested += OnConnectionTokenRequested;
        _viewModel.ManageDataRequested += OnManageDataRequested;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        ApplyNavigationVisibilityBySection();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowCaptionTheme();
    }

    private void ApplyWindowCaptionTheme()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            // COLORREF format: 0x00BBGGRR
            var captionColor = 0x00FAF7F5; // #F5F7FA
            var textColor = 0x001F1F1F; // #1F1F1F
            var borderColor = 0x00E9DED5; // #D5DEE9

            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
        }
        catch
        {
            // Ignore on unsupported OS/window managers.
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentView))
        {
            ApplyNavigationVisibilityBySection();
        }
    }

    private void BtnToggleNav_OnClick(object sender, RoutedEventArgs e)
    {
        _isNavCollapsed = !_isNavCollapsed;

        if (_isNavCollapsed)
        {
            NavColumn.Width = new GridLength(52);
            NavHeaderPanel.Visibility = Visibility.Collapsed;
            NavMenuScroll.Visibility = Visibility.Collapsed;
            NavFooterPanel.Visibility = Visibility.Collapsed;
            BtnToggleNav.Content = "▶";
        }
        else
        {
            NavColumn.Width = new GridLength(260);
            NavHeaderPanel.Visibility = Visibility.Visible;
            NavMenuScroll.Visibility = Visibility.Visible;
            NavFooterPanel.Visibility = Visibility.Visible;
            BtnToggleNav.Content = "◀";
        }
    }

    private void ApplyNavigationVisibilityBySection()
    {
        var isLogin = ReferenceEquals(_viewModel.CurrentView, _viewModel.Login);
        if (isLogin)
        {
            NavColumn.Width = new GridLength(52);
            NavHeaderPanel.Visibility = Visibility.Collapsed;
            NavMenuScroll.Visibility = Visibility.Collapsed;
            NavFooterPanel.Visibility = Visibility.Collapsed;
            BtnToggleNav.Visibility = Visibility.Collapsed;
            _isNavCollapsed = true;
            return;
        }

        BtnToggleNav.Visibility = Visibility.Visible;
        _isNavCollapsed = false;

        if (_isNavCollapsed)
        {
            NavColumn.Width = new GridLength(52);
            NavHeaderPanel.Visibility = Visibility.Collapsed;
            NavMenuScroll.Visibility = Visibility.Collapsed;
            NavFooterPanel.Visibility = Visibility.Collapsed;
            BtnToggleNav.Content = "▶";
        }
        else
        {
            NavColumn.Width = new GridLength(260);
            NavHeaderPanel.Visibility = Visibility.Visible;
            NavMenuScroll.Visibility = Visibility.Visible;
            NavFooterPanel.Visibility = Visibility.Visible;
            BtnToggleNav.Content = "◀";
        }
    }

    private void OnHelpRequested(object? sender, EventArgs e)
    {
        var helpWindow = new HelpViewerWindow
        {
            Owner = this
        };

        helpWindow.ShowDialog();
    }

    private void OnConnectionTokenRequested(object? sender, EventArgs e)
    {
        var tokenDialog = new ConnectionTokenDialog(new DbConnectionFactory(), new ConnectionBootstrapTokenService())
        {
            Owner = this
        };

        tokenDialog.ShowDialog();
    }

    private void OnManageDataRequested(object? sender, EventArgs e)
    {
        var dialog = new ManageDataDialog(
            _viewModel.Services.WikiCatalog,
            _viewModel.Services.AdminCatalog,
            _viewModel.IsAdminUser)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }
}

