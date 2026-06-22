using System.Windows.Controls;
using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Wpf.Views.Dialogs;

namespace ExportAzureWiki.Wpf.Views;

public partial class ProvidersView : UserControl
{
    public ProvidersView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => ApplyColumnHeaders();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        ApplyColumnHeaders();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(ApplyColumnHeaders);
    }

    private void ApplyColumnHeaders()
    {
        if (DataContext is not ProvidersViewModel vm)
        {
            return;
        }

        colOauthProvider.Header = vm.ProviderHeader;
        colOauthName.Header = vm.NameHeader;
        colOauthEnabled.Header = vm.EnabledText;
        colOauthRedirect.Header = vm.RedirectHeader;

        colAiDisplay.Header = vm.DisplayHeader;
        colAiProvider.Header = vm.ProviderHeader;
        colAiModel.Header = vm.ModelHeader;
        colAiEnabled.Header = vm.EnabledText;
        colAiDefault.Header = vm.DefaultText;
    }

    private async void BtnNewOAuth_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel vm)
        {
            return;
        }

        var draft = vm.CreateOAuthDraftForNew();
        var dialog = new OAuthProviderEditDialog(draft, isNew: true)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await vm.SaveOAuthFromDialogAsync(dialog.Result, isNew: true);
        }
    }

    private async void BtnEditOAuth_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel vm)
        {
            return;
        }

        var draft = vm.CreateOAuthDraftFromSelected();
        if (draft == null)
        {
            return;
        }

        var dialog = new OAuthProviderEditDialog(draft, isNew: false)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            var confirmation = MessageBox.Show(
                string.Format(
                    AppText.S("wpf.confirm.edit_oauth.message", "Save changes to provider '{0}'?"),
                    dialog.Result.DisplayName),
                AppText.S("wpf.confirm.edit_oauth.title", "Confirm Changes"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            await vm.SaveOAuthFromDialogAsync(dialog.Result, isNew: false);
        }
    }

    private async void BtnNewAi_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel vm)
        {
            return;
        }

        var draft = vm.CreateAiDraftForNew();
        var dialog = new AiProviderEditDialog(draft, isNew: true, vm.AiProviderProbe)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await vm.SaveAiFromDialogAsync(dialog.Result);
        }
    }

    private async void BtnEditAi_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProvidersViewModel vm)
        {
            return;
        }

        var draft = vm.CreateAiDraftFromSelected();
        if (draft == null)
        {
            return;
        }

        var dialog = new AiProviderEditDialog(draft, isNew: false, vm.AiProviderProbe)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            var confirmation = MessageBox.Show(
                string.Format(
                    AppText.S("wpf.confirm.edit_ai.message", "Save changes to AI provider '{0}'?"),
                    dialog.Result.DisplayName),
                AppText.S("wpf.confirm.edit_ai.title", "Confirm Changes"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            await vm.SaveAiFromDialogAsync(dialog.Result);
        }
    }
}

