using System.Windows.Controls;
using System.Windows;
using ExportAzureWiki.Localization;
using ExportAzureWiki.Wpf.ViewModels;
using ExportAzureWiki.Wpf.Views.Dialogs;

namespace ExportAzureWiki.Wpf.Views;

public partial class PermissionsView : UserControl
{
    public PermissionsView()
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
        if (DataContext is not PermissionsViewModel vm)
        {
            return;
        }

        colPolicyIdentity.Header = vm.IdentityHeader;
        colPolicyType.Header = vm.TypeHeader;
        colPolicyAdmin.Header = vm.AdminText;
        colPolicyWikiRules.Header = vm.WikiRulesHeader;
        colPolicyManageWikis.Header = vm.ManageWikisText;
        colPolicyManageUsers.Header = vm.ManageUsersText;
        colPolicyManagePermissions.Header = vm.ManagePermissionsText;

        colRuleWikiId.Header = vm.WikiIdHeader;
        colRuleStartPoints.Header = vm.StartPointsHeader;
        colRuleView.Header = vm.ViewHeader;
        colRuleComment.Header = vm.CommentHeader;
        colRuleWord.Header = vm.WordHeader;
        colRulePdf.Header = vm.PdfHeader;
        colRuleLetterhead.Header = vm.LetterheadHeader;
    }

    private void BtnAddRule_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PermissionsViewModel vm || vm.SelectedPolicy == null)
        {
            return;
        }

        var draft = vm.CreateRuleDraftForNew();
        var dialog = new WikiRuleEditDialog(draft, vm.Wikis, vm.LoadWikiSelectablePathsAsync, isNew: true)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            vm.ApplyRuleFromDialog(dialog.Result, isNew: true);
        }
    }

    private void BtnEditRule_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PermissionsViewModel vm || vm.SelectedRule == null)
        {
            return;
        }

        var draft = vm.CreateRuleDraftFromSelected();
        if (draft == null)
        {
            return;
        }

        var dialog = new WikiRuleEditDialog(draft, vm.Wikis, vm.LoadWikiSelectablePathsAsync, isNew: false)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            vm.ApplyRuleFromDialog(dialog.Result, isNew: false);
        }
    }
}

