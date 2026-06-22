using System.Windows.Controls;
using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Wpf.Views.Dialogs;

namespace ExportAzureWiki.Wpf.Views;

public partial class WikiManagementView : UserControl
{
    public WikiManagementView()
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
        if (DataContext is not WikiManagementViewModel vm)
        {
            return;
        }

        colWikiName.Header = vm.NameText;
        colWikiPlatform.Header = vm.PlatformText;
        colWikiOrganization.Header = vm.OrganizationHeader;
        colWikiProject.Header = vm.ProjectHeader;
        colWikiWiki.Header = vm.WikiHeader;
        colWikiRepository.Header = vm.RepositoryHeader;
        colWikiStartPoints.Header = vm.StartPointsHeader;
    }

    private async void BtnNew_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WikiManagementViewModel vm)
        {
            return;
        }

        try
        {
            var draft = vm.CreateDraftForNew();
            var dialog = new WikiEditDialog(draft, vm, isNew: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                var saved = await vm.SaveFromDialogAsync(dialog.Result, isNew: true);
                if (saved)
                {
                    await vm.LoadAsync();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                AppText.S("common.error", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void BtnEdit_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WikiManagementViewModel vm)
        {
            return;
        }

        var draft = vm.CreateDraftFromSelected();
        if (draft == null)
        {
            return;
        }

        var dialog = new WikiEditDialog(draft, vm, isNew: false)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            var confirmation = MessageBox.Show(
                string.Format(
                    AppText.S("wpf.confirm.edit_wiki.message", "Save changes to wiki '{0}'?"),
                    dialog.Result.Name),
                AppText.S("wpf.confirm.edit_wiki.title", "Confirm Changes"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            await vm.SaveFromDialogAsync(dialog.Result, isNew: false);
        }
    }
}

