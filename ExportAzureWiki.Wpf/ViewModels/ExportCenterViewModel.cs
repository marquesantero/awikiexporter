using ExportAzureWiki.Wpf.Commands;
using ExportAzureWiki.Core.Services;
using Microsoft.Win32;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class ExportCenterViewModel : ViewModelBase
{
    private readonly WorkspaceViewModel _workspaceViewModel;
    private readonly IDocumentExportService _exportService;
    private string _status = AppText.S("wpf.export.status.ready", "Ready");
    private bool _includeAdditionalPages = true;
    private bool _refreshCacheBeforeExport;
    private ExportScope _selectedScope = ExportScope.CurrentDocument;
    private readonly RelayCommand _clearAdditionalPagesCommand;

    public ExportCenterViewModel(WorkspaceViewModel workspaceViewModel, IDocumentExportService exportService)
    {
        _workspaceViewModel = workspaceViewModel;
        _exportService = exportService;
        ExportWordCommand = new RelayCommand(async () => await ExportWordAsync());
        ExportPdfCommand = new RelayCommand(async () => await ExportPdfAsync());
        _clearAdditionalPagesCommand = new RelayCommand(ClearAdditionalPages);
        _workspaceViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.AdditionalPagesCount))
            {
                OnPropertyChanged(nameof(AdditionalPagesInfo));
            }
        };
    }

    public string Info => AppText.S(
        "wpf.export.info",
        "Exports currently use the selected page from Workspace. Multi-page orchestration will be migrated next.");
    public string HeaderTitle => AppText.S("wpf.export.header.title", "Export Center");
    public string ExportWordText => AppText.S("wpf.export.word", "Export to Word");
    public string ExportPdfText => AppText.S("wpf.export.pdf", "Export to PDF");
    public string CurrentDocumentText => AppText.S("wpf.export.scope.current", "Current page/file");
    public string AllLoadedPagesText => AppText.S("wpf.export.scope.all_loaded", "All loaded wiki pages");
    public string IncludeAiPagesText => AppText.S("wpf.export.include_ai_pages", "Include AI extra pages");
    public string RefreshCacheText => AppText.S("main.option.refresh_cache", "Refresh Cache");
    public string ClearAiPagesText => AppText.S("wpf.export.clear_ai_pages", "Clear AI pages");
    public string AdditionalPagesInfo => string.Format(
        AppText.S("wpf.export.additional_pages_info", "AI extra pages: {0}"),
        _workspaceViewModel.AdditionalPagesCount);

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public bool IncludeAdditionalPages
    {
        get => _includeAdditionalPages;
        set
        {
            _includeAdditionalPages = value;
            OnPropertyChanged();
        }
    }

    public bool RefreshCacheBeforeExport
    {
        get => _refreshCacheBeforeExport;
        set
        {
            _refreshCacheBeforeExport = value;
            OnPropertyChanged();
        }
    }

    public ExportScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            _selectedScope = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurrentScope));
            OnPropertyChanged(nameof(IsAllLoadedPagesScope));
        }
    }

    public bool IsCurrentScope
    {
        get => SelectedScope == ExportScope.CurrentDocument;
        set
        {
            if (value)
            {
                SelectedScope = ExportScope.CurrentDocument;
            }
        }
    }

    public bool IsAllLoadedPagesScope
    {
        get => SelectedScope == ExportScope.AllLoadedPages;
        set
        {
            if (value)
            {
                SelectedScope = ExportScope.AllLoadedPages;
            }
        }
    }

    public System.Windows.Input.ICommand ExportWordCommand { get; }
    public System.Windows.Input.ICommand ExportPdfCommand { get; }
    public System.Windows.Input.ICommand ClearAdditionalPagesCommand => _clearAdditionalPagesCommand;

    private async Task ExportWordAsync()
    {
        var html = await _workspaceViewModel.BuildExportHtmlAsync(
            IncludeAdditionalPages,
            SelectedScope,
            RefreshCacheBeforeExport);
        if (string.IsNullOrWhiteSpace(html))
        {
            Status = AppText.S("wpf.export.status.no_content", "No page selected/content loaded.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = AppText.S("wpf.export.dialog.word.filter", "Word (*.docx)|*.docx"),
            FileName = AppText.S("wpf.export.dialog.word.filename", "wiki-export.docx")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _exportService.ExportToWordAsync(html, dialog.FileName, applyWordFineTune: false, refreshImageCache: RefreshCacheBeforeExport);
            Status = string.Format(
                AppText.S("wpf.export.status.word_success", "Word exported: {0}"),
                dialog.FileName);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.export.status.error", "Error: {0}"), ex.Message);
        }
    }

    private async Task ExportPdfAsync()
    {
        var html = await _workspaceViewModel.BuildExportHtmlAsync(
            IncludeAdditionalPages,
            SelectedScope,
            RefreshCacheBeforeExport);
        if (string.IsNullOrWhiteSpace(html))
        {
            Status = AppText.S("wpf.export.status.no_content", "No page selected/content loaded.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = AppText.S("wpf.export.dialog.pdf.filter", "PDF (*.pdf)|*.pdf"),
            FileName = AppText.S("wpf.export.dialog.pdf.filename", "wiki-export.pdf")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _exportService.ExportToPdfAsync(html, dialog.FileName);
            Status = string.Format(
                AppText.S("wpf.export.status.pdf_success", "PDF exported: {0}"),
                dialog.FileName);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.export.status.error", "Error: {0}"), ex.Message);
        }
    }

    private void ClearAdditionalPages()
    {
        _workspaceViewModel.ClearAdditionalPages();
        OnPropertyChanged(nameof(AdditionalPagesInfo));
        Status = AppText.S("wpf.export.status.cleared_ai_pages", "AI extra pages cleared.");
    }
}

