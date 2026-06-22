using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Wpf.Commands;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class PermissionsViewModel : ViewModelBase
{
    private readonly IAdminCatalogService _service;
    private readonly IWikiPageBrowserService _wikiPageBrowserService;
    private bool _isLoading;
    private string _status = AppText.S("wpf.permissions.status.ready", "Ready");
    private AccessPolicy? _selectedPolicy;
    private WikiAccessRule? _selectedRule;
    private IReadOnlyList<WikiConfiguration> _wikis = [];

    public ObservableCollection<AccessPolicy> Policies { get; } = [];
    public ObservableCollection<WikiAccessRule> WikiRules { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RemoveWikiRuleCommand { get; }

    public PermissionsViewModel(IAdminCatalogService service, IWikiPageBrowserService wikiPageBrowserService)
    {
        _service = service;
        _wikiPageBrowserService = wikiPageBrowserService;
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        SaveCommand = new RelayCommand(async () => await SaveAsync(), () => SelectedPolicy != null);
        RemoveWikiRuleCommand = new RelayCommand(RemoveWikiRule, () => SelectedRule != null);
    }

    public string Title => AppText.S("wpf.permissions.title", "Permissions");
    public string RefreshText => AppText.S("common.refresh", "Refresh");
    public string SaveText => AppText.S("common.save", "Save");
    public string PoliciesText => AppText.S("wpf.permissions.policies", "Policies");
    public string PolicyEditorText => AppText.S("wpf.permissions.policy_editor", "Policy editor");
    public string WikiRulesText => AppText.S("wpf.permissions.wiki_rules", "Wiki rules");
    public string AddWikiRuleText => AppText.S("wpf.permissions.add_rule", "Add rule");
    public string EditWikiRuleText => AppText.S("wpf.permissions.edit_rule", "Edit");
    public string RemoveSelectedText => AppText.S("wpf.permissions.remove_selected", "Delete");
    public string AdminText => AppText.S("common.admin", "Admin");
    public string ManageWikisText => AppText.S("wpf.permissions.manage_wikis", "Manage Wikis");
    public string ManageUsersText => AppText.S("wpf.permissions.manage_users", "Manage Users");
    public string ManagePermissionsText => AppText.S("wpf.permissions.manage_permissions", "Manage Permissions");
    public string IdentityHeader => AppText.S("wpf.permissions.col.identity", "Identity");
    public string TypeHeader => AppText.S("wpf.permissions.col.type", "Type");
    public string WikiRulesHeader => AppText.S("wpf.permissions.col.wiki_rules", "Wiki Rules");
    public string WikiIdHeader => AppText.S("wpf.permissions.col.wiki_id", "WikiId");
    public string StartPointsHeader => AppText.S("wpf.permissions.col.start_points", "Start Points");
    public string ViewHeader => AppText.S("wpf.permissions.col.view", "View");
    public string CommentHeader => AppText.S("wpf.permissions.col.comment", "Comment");
    public string WordHeader => AppText.S("wpf.permissions.col.word", "Word");
    public string PdfHeader => AppText.S("wpf.permissions.col.pdf", "PDF");
    public string LetterheadHeader => AppText.S("wpf.permissions.col.letterhead", "Letterhead");

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public AccessPolicy? SelectedPolicy
    {
        get => _selectedPolicy;
        set
        {
            _selectedPolicy = value;
            OnPropertyChanged();
            ReloadWikiRules();
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public WikiAccessRule? SelectedRule
    {
        get => _selectedRule;
        set
        {
            _selectedRule = value;
            OnPropertyChanged();
            (RemoveWikiRuleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public IReadOnlyList<WikiConfiguration> Wikis
    {
        get => _wikis;
        private set
        {
            _wikis = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        try
        {
            IsLoading = true;
            Status = AppText.S("wpf.permissions.status.loading", "Loading policies...");
            Policies.Clear();
            WikiRules.Clear();

            var policies = await _service.LoadAccessPoliciesAsync();
            Wikis = await _service.LoadWikisAsync();

            foreach (var p in policies)
            {
                Policies.Add(p);
            }

            SelectedPolicy = Policies.FirstOrDefault();
            Status = string.Format(AppText.S("wpf.permissions.status.loaded", "Loaded {0} policy(ies)"), Policies.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.permissions.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ReloadWikiRules()
    {
        WikiRules.Clear();
        if (SelectedPolicy?.Wikis == null)
        {
            return;
        }

        foreach (var rule in SelectedPolicy.Wikis)
        {
            WikiRules.Add(rule);
        }
        SelectedRule = WikiRules.FirstOrDefault();
    }

    public WikiAccessRule CreateRuleDraftForNew()
    {
        var defaultWikiId = Wikis.FirstOrDefault()?.Id.ToString() ?? string.Empty;
        return new WikiAccessRule
        {
            WikiId = defaultWikiId,
            CanView = true
        };
    }

    public WikiAccessRule? CreateRuleDraftFromSelected()
    {
        if (SelectedRule == null)
        {
            return null;
        }

        return new WikiAccessRule
        {
            WikiId = SelectedRule.WikiId,
            StartPoints = SelectedRule.StartPoints,
            CanView = SelectedRule.CanView,
            CanComment = SelectedRule.CanComment,
            CanExportWord = SelectedRule.CanExportWord,
            CanExportPdf = SelectedRule.CanExportPdf,
            CanUseLetterhead = SelectedRule.CanUseLetterhead
        };
    }

    public void ApplyRuleFromDialog(WikiAccessRule rule, bool isNew)
    {
        if (SelectedPolicy == null)
        {
            return;
        }

        if (isNew)
        {
            SelectedPolicy.Wikis.Add(rule);
            WikiRules.Add(rule);
            SelectedRule = rule;
            Status = AppText.S("wpf.permissions.status.rule_added", "Wiki rule added.");
            return;
        }

        if (SelectedRule == null)
        {
            return;
        }

        SelectedRule.WikiId = rule.WikiId;
        SelectedRule.StartPoints = rule.StartPoints;
        SelectedRule.CanView = rule.CanView;
        SelectedRule.CanComment = rule.CanComment;
        SelectedRule.CanExportWord = rule.CanExportWord;
        SelectedRule.CanExportPdf = rule.CanExportPdf;
        SelectedRule.CanUseLetterhead = rule.CanUseLetterhead;
        OnPropertyChanged(nameof(WikiRules));
        Status = AppText.S("wpf.permissions.status.rule_updated", "Wiki rule updated.");
    }

    private void RemoveWikiRule()
    {
        if (SelectedPolicy == null || SelectedRule == null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            AppText.S("wpf.confirm.delete_wiki_rule.message", "Delete selected wiki rule?"),
            AppText.S("wpf.confirm.delete_wiki_rule.title", "Confirm Deletion"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            Status = AppText.S("wpf.confirm.delete_wiki_rule.canceled", "Wiki rule deletion canceled.");
            return;
        }

        SelectedPolicy.Wikis.Remove(SelectedRule);
        WikiRules.Remove(SelectedRule);
        SelectedRule = WikiRules.FirstOrDefault();
        Status = AppText.S("wpf.permissions.status.rule_removed", "Wiki rule removed.");
    }

    private async Task SaveAsync()
    {
        if (SelectedPolicy == null)
        {
            return;
        }

        try
        {
            SelectedPolicy.LastModifiedAt = DateTime.Now;
            await _service.SaveAccessPolicyAsync(SelectedPolicy);
            Status = AppText.S("wpf.permissions.status.saved", "Policy saved.");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.permissions.status.error", "Error: {0}"), ex.Message);
        }
    }

    public async Task<IReadOnlyList<string>> LoadWikiSelectablePathsAsync(string wikiId)
    {
        var wiki = Wikis.FirstOrDefault(w => string.Equals(w.Id, wikiId, StringComparison.OrdinalIgnoreCase));
        if (wiki == null)
        {
            return [];
        }

        var pages = await _wikiPageBrowserService.GetPagesAsync(wiki);
        var allPaths = pages
            .Select(p => p.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var configuredStartPoints = ParseStartPoints(wiki.RootPath);
        if (configuredStartPoints.Count == 0)
        {
            return allPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var filtered = allPaths
            .Where(candidate => configuredStartPoints.Any(sp =>
                string.Equals(candidate, sp, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(sp + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return filtered;
    }

    private static List<string> ParseStartPoints(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return [];
        }

        return rootPath
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePath(string path)
    {
        var value = path.Trim();
        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        if (value != "/")
        {
            value = value.TrimEnd('/');
        }

        return value;
    }
}

