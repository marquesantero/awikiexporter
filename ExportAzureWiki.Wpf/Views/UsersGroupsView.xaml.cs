using System.Windows.Controls;
using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Wpf.Views.Dialogs;

namespace ExportAzureWiki.Wpf.Views;

public partial class UsersGroupsView : UserControl
{
    public UsersGroupsView()
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
        if (DataContext is not UsersGroupsViewModel vm)
        {
            return;
        }

        colUsersId.Header = vm.IdHeader;
        colUsersUsername.Header = vm.UsernameHeader;
        colUsersEmail.Header = vm.EmailHeader;
        colUsersDisplay.Header = vm.DisplayNameHeader;
        colUsersActive.Header = vm.ActiveHeader;

        colGroupsId.Header = vm.IdHeader;
        colGroupsName.Header = vm.NameHeader;
        colGroupsDescription.Header = vm.DescriptionHeader;
        colGroupsSource.Header = vm.SourceHeader;
        colGroupsSystem.Header = vm.SystemHeader;
    }

    private async void BtnNewUser_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UsersGroupsViewModel vm)
        {
            return;
        }

        var draft = vm.CreateUserDraftForNew();
        var dialog = new UserEditDialog(draft, isNew: true, vm.LoadExternalProvidersAsync, vm.SearchExternalUsersAsync)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            var confirmation = MessageBox.Show(
                string.Format(
                    AppText.S("wpf.confirm.edit_user.message", "Save changes to user '{0}'?"),
                    string.IsNullOrWhiteSpace(dialog.Result.DisplayName) ? dialog.Result.Username : dialog.Result.DisplayName),
                AppText.S("wpf.confirm.edit_user.title", "Confirm Changes"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            await vm.SaveUserFromDialogAsync(dialog.Result, dialog.PlainPassword);
        }
    }

    private async void BtnEditUser_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UsersGroupsViewModel vm)
        {
            return;
        }

        var draft = vm.CreateUserDraftFromSelected();
        if (draft == null)
        {
            return;
        }

        var dialog = new UserEditDialog(draft, isNew: false, vm.LoadExternalProvidersAsync, vm.SearchExternalUsersAsync)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await vm.SaveUserFromDialogAsync(dialog.Result, dialog.PlainPassword);
        }
    }

    private async void BtnNewGroup_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UsersGroupsViewModel vm)
        {
            return;
        }

        var draft = vm.CreateGroupDraftForNew();
        var dialog = new GroupEditDialog(draft, isNew: true)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            await vm.SaveGroupFromDialogAsync(dialog.Result);
        }
    }

    private async void BtnEditGroup_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UsersGroupsViewModel vm)
        {
            return;
        }

        var draft = vm.CreateGroupDraftFromSelected();
        if (draft == null)
        {
            return;
        }

        var dialog = new GroupEditDialog(draft, isNew: false)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            var confirmation = MessageBox.Show(
                string.Format(
                    AppText.S("wpf.confirm.edit_group.message", "Save changes to group '{0}'?"),
                    dialog.Result.Name),
                AppText.S("wpf.confirm.edit_group.title", "Confirm Changes"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            await vm.SaveGroupFromDialogAsync(dialog.Result);
        }
    }
}

