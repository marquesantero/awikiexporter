using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class WikiRuleEditDialog : Window
{
    private readonly WikiRuleEditModel _model;
    private readonly IReadOnlyList<WikiConfiguration> _wikis;
    private readonly Func<string, Task<IReadOnlyList<string>>>? _loadPathsForWikiAsync;

    public WikiRuleEditDialog(
        WikiAccessRule source,
        IReadOnlyList<WikiConfiguration> wikis,
        Func<string, Task<IReadOnlyList<string>>>? loadPathsForWikiAsync,
        bool isNew)
    {
        InitializeComponent();
        _wikis = wikis ?? [];
        _loadPathsForWikiAsync = loadPathsForWikiAsync;
        _model = new WikiRuleEditModel(source, _wikis, isNew);
        DataContext = _model;
    }

    public WikiAccessRule Result => _model.ToWikiAccessRule();

    private void BtnSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_model.WikiId))
        {
            MessageBox.Show(
                AppText.S("permissions.validation.select_wiki", "Select a wiki."),
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

    private async void BtnSelectStartPoints_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_model.WikiId))
        {
            MessageBox.Show(
                AppText.S("permissions.validation.select_wiki", "Select a wiki."),
                AppText.S("common.validation", "Validation"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var selectedWiki = _wikis.FirstOrDefault(w => string.Equals(w.Id, _model.WikiId, StringComparison.OrdinalIgnoreCase));
        if (selectedWiki == null)
        {
            MessageBox.Show(
                AppText.S("permissions.validation.select_wiki", "Select a wiki."),
                AppText.S("common.validation", "Validation"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IReadOnlyList<string> options;
        if (_loadPathsForWikiAsync != null)
        {
            options = await _loadPathsForWikiAsync(_model.WikiId);
        }
        else
        {
            options = ParseConfiguredStartPoints(selectedWiki.RootPath);
        }

        if (options.Count == 0)
        {
            MessageBox.Show(
                AppText.S("permissions.new.start_points.empty", "No pages were found for this wiki."),
                AppText.S("common.information", "Information"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selected = ParseConfiguredStartPoints(_model.StartPoints).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var picker = new StartPointSelectionDialog(options, selected)
        {
            Owner = this
        };

        if (picker.ShowDialog() == true)
        {
            _model.StartPoints = string.Join("|", picker.SelectedStartPoints);
        }
    }

    private static List<string> ParseConfiguredStartPoints(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return [];
        }

        return rootPath
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class WikiRuleEditModel : INotifyPropertyChanged
{
    private string _wikiId;
    private string _startPoints;
    private bool _canView;
    private bool _canComment;
    private bool _canExportWord;
    private bool _canExportPdf;
    private bool _canUseLetterhead;

    public WikiRuleEditModel(WikiAccessRule source, IReadOnlyList<WikiConfiguration> wikis, bool isNew)
    {
        _wikiId = source.WikiId;
        _startPoints = source.StartPoints;
        _canView = source.CanView;
        _canComment = source.CanComment;
        _canExportWord = source.CanExportWord;
        _canExportPdf = source.CanExportPdf;
        _canUseLetterhead = source.CanUseLetterhead;
        DialogTitle = isNew
            ? AppText.S("wpf.permissions.rule.dialog.new.title", "New Wiki Rule")
            : AppText.S("wpf.permissions.rule.dialog.edit.title", "Edit Wiki Rule");
        WikiOptions = wikis
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(w => new WikiOption { Id = w.Id, DisplayName = w.Name })
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string DialogTitle { get; }
    public string WikiIdText => AppText.S("wpf.permissions.col.wiki_id", "WikiId");
    public string StartPointsText => AppText.S("wpf.permissions.col.start_points", "Start Points");
    public string ViewText => AppText.S("wpf.permissions.col.view", "View");
    public string CommentText => AppText.S("wpf.permissions.col.comment", "Comment");
    public string WordText => AppText.S("wpf.permissions.col.word", "Word");
    public string PdfText => AppText.S("wpf.permissions.col.pdf", "PDF");
    public string LetterheadText => AppText.S("wpf.permissions.col.letterhead", "Letterhead");
    public string SaveText => AppText.S("common.save", "Save");
    public string CancelText => AppText.S("common.cancel", "Cancel");
    public string SelectStartPointsText => AppText.S("wpf.common.pages", "Pages");
    public string HelpText => AppText.S("wpf.permissions.rule.dialog.help", "Use Start Points separated by '|'.");
    public IReadOnlyList<WikiOption> WikiOptions { get; }

    public string WikiId
    {
        get => _wikiId;
        set => Set(ref _wikiId, value);
    }

    public string StartPoints
    {
        get => _startPoints;
        set => Set(ref _startPoints, value);
    }

    public bool CanView
    {
        get => _canView;
        set => Set(ref _canView, value);
    }

    public bool CanComment
    {
        get => _canComment;
        set => Set(ref _canComment, value);
    }

    public bool CanExportWord
    {
        get => _canExportWord;
        set => Set(ref _canExportWord, value);
    }

    public bool CanExportPdf
    {
        get => _canExportPdf;
        set => Set(ref _canExportPdf, value);
    }

    public bool CanUseLetterhead
    {
        get => _canUseLetterhead;
        set => Set(ref _canUseLetterhead, value);
    }

    public WikiAccessRule ToWikiAccessRule()
    {
        return new WikiAccessRule
        {
            WikiId = WikiId?.Trim() ?? string.Empty,
            StartPoints = StartPoints?.Trim() ?? string.Empty,
            CanView = CanView,
            CanComment = CanComment,
            CanExportWord = CanExportWord,
            CanExportPdf = CanExportPdf,
            CanUseLetterhead = CanUseLetterhead
        };
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
}

public sealed class WikiOption
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
