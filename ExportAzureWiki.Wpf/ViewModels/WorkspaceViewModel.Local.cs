using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Input;
using ExportAzureWiki.Core.Authentication;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Wpf.Commands;
using ExportAzureWiki.Wpf.Services;
using Markdig;
using HtmlAgilityPack;
using Microsoft.Win32;
using ExportAzureWiki;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public async Task LoadMarkdownFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = AppText.S("wpf.workspace.dialog.open_markdown.filter", "Markdown (*.md;*.markdown)|*.md;*.markdown|Text (*.txt)|*.txt|All files (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsLoading = true;
            IsLoadingPages = true;
            BusyMessage = AppText.S("wpf.workspace.busy.loading_file", "Loading file...");
            _isLocalMarkdownMode = true;
            _localFolderMode = false;
            ShowLoadLocalPagesButton = false;
            _localFolderFiles.Clear();
            _localFolderOrder.Clear();
            _currentLocalMarkdownPath = dialog.FileName;
            _renderedPages.Clear();
            LocalPageTree.Clear();
            _currentRenderedPageIndex = -1;

            var rendered = await _wikiPageRenderService.RenderLocalMarkdownAsync(dialog.FileName);
            if (rendered == null || string.IsNullOrWhiteSpace(rendered.HtmlFilePath))
            {
                CurrentPageMarkdown = string.Empty;
                CurrentPageHtml = string.Empty;
                CurrentDocumentTitle = string.Empty;
                _currentPageLastUpdated = null;
                OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
                PageNavigationText = string.Empty;
                RaiseNavigationStateChanged();
                Status = AppText.S("wpf.workspace.status.error_local_markdown", "Unable to load local markdown.");
                return;
            }

            _renderedPages.Add(rendered);
            LocalPageTree.Add(new LocalPageNodeViewModel
            {
                Title = Path.GetFileName(dialog.FileName),
                RenderedPath = rendered.Path
            });
            await LoadRenderedPageAsync(0);
            SelectedPageNode = null;
            Status = string.Format(
                AppText.S("wpf.workspace.status.loaded_file", "Loaded file: {0}"),
                Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_loading_file", "Error loading file: {0}"),
                ex.Message);
        }
        finally
        {
            IsLoadingPages = false;
            BusyMessage = string.Empty;
            IsLoading = false;
        }
    }

    public async Task LoadMarkdownFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadLocalFolderAsync(dialog.FolderName, Path.GetFileName(dialog.FolderName.TrimEnd('\\', '/')));
    }

    public async Task LoadMarkdownZipAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = AppText.S("wpf.workspace.dialog.open_zip.filter", "Zip archive (*.zip)|*.zip"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // Extract to a per-archive folder under the app-owned cache root
            // (never system TEMP), then reuse the folder flow.
            var extractRoot = WikiCachePaths.ZipImport(Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractRoot);
            await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(dialog.FileName, extractRoot, overwriteFiles: true));
            await LoadLocalFolderAsync(extractRoot, Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_loading_folder", "Error loading folder: {0}"),
                ex.Message);
        }
    }

    private async Task LoadLocalFolderAsync(string folder, string sourceLabel)
    {
        try
        {
            IsLoading = true;
            IsLoadingPages = true;
            BusyMessage = AppText.S("wpf.workspace.busy.loading_folder", "Loading folder...");
            _isLocalMarkdownMode = true;
            _localFolderMode = true;
            _currentLocalMarkdownPath = null;
            _renderedPages.Clear();
            _generatedPageMarkdown.Clear();
            _localFolderFiles.Clear();
            _localFolderOrder.Clear();
            LocalPageTree.Clear();
            _currentRenderedPageIndex = -1;

            // Recursively gather every Markdown file, ordered by relative path so
            // the navigation mirrors the folder tree.
            var files = await Task.Run(() => Directory
                .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList());

            if (files.Count == 0)
            {
                Status = AppText.S("wpf.workspace.status.folder_empty", "No Markdown files found in the selected folder.");
                return;
            }

            // Build the file map + tree immediately (instant for large folders);
            // pages are rendered lazily on selection.
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(folder, file).Replace('\\', '/');
                _localFolderFiles[relativePath] = file;
                _localFolderOrder.Add(relativePath);
            }

            BuildLocalTree(_localFolderOrder);

            if (_localFolderOrder.Count == 1)
            {
                // Single page: open it immediately.
                ShowLoadLocalPagesButton = false;
                var firstIndex = await EnsureLocalPageRenderedAsync(_localFolderOrder[0]);
                if (firstIndex >= 0)
                {
                    await LoadRenderedPageAsync(firstIndex);
                }
            }
            else
            {
                // Multiple pages: show only the tree (it may be large). The user
                // checks the pages they want and clicks "Load pages".
                ShowLoadLocalPagesButton = true;
                CurrentPageHtml = string.Empty;
                CurrentPageMarkdown = string.Empty;
                CurrentDocumentTitle = string.Empty;
                _currentPageLastUpdated = null;
                OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
                PageNavigationText = string.Empty;
                _currentRenderedPageIndex = -1;
                RaiseNavigationStateChanged();
            }

            SelectedPageNode = null;
            Status = string.Format(
                AppText.S("wpf.workspace.status.loaded_folder", "Loaded {0} file(s) from folder: {1}"),
                _localFolderOrder.Count, sourceLabel);
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_loading_folder", "Error loading folder: {0}"),
                ex.Message);
        }
        finally
        {
            IsLoadingPages = false;
            BusyMessage = string.Empty;
            IsLoading = false;
        }
    }

    /// <summary>
    /// Builds the hierarchical "Local" tab tree from the relative paths of the
    /// rendered local pages (folders become parent nodes, files become leaves
    /// whose RenderedPath matches the rendered-pages entry).
    /// </summary>
    private void BuildLocalTree(IReadOnlyList<string> relativePaths)
    {
        LocalPageTree.Clear();
        foreach (var node in BuildLocalTreeNodes(relativePaths))
        {
            LocalPageTree.Add(node);
        }
    }

    /// <summary>
    /// Pure tree builder: turns the relative paths of local pages into a
    /// hierarchy of <see cref="LocalPageNodeViewModel"/> (folders become parent
    /// nodes, files become leaves whose RenderedPath equals the input path).
    /// Extracted as a static so it can be unit-tested without constructing the
    /// whole view model.
    /// </summary>
    internal static IReadOnlyList<LocalPageNodeViewModel> BuildLocalTreeNodes(IReadOnlyList<string> relativePaths)
    {
        var roots = new List<LocalPageNodeViewModel>();
        var folderByPath = new Dictionary<string, LocalPageNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            IList<LocalPageNodeViewModel> currentLevel = roots;
            LocalPageNodeViewModel? parent = null;
            var pathSoFar = string.Empty;

            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                pathSoFar = string.IsNullOrEmpty(pathSoFar) ? segment : $"{pathSoFar}/{segment}";

                if (i == segments.Length - 1)
                {
                    currentLevel.Add(new LocalPageNodeViewModel
                    {
                        Title = segment,
                        RenderedPath = relativePath,
                        Parent = parent
                    });
                }
                else
                {
                    if (!folderByPath.TryGetValue(pathSoFar, out var folder))
                    {
                        folder = new LocalPageNodeViewModel { Title = segment, RenderedPath = null, Parent = parent };
                        folderByPath[pathSoFar] = folder;
                        currentLevel.Add(folder);
                    }

                    parent = folder;
                    currentLevel = folder.Children;
                }
            }
        }

        return roots;
    }

    private void SaveSession(WorkspaceTabSession session)
    {
        session.RenderedPages.Clear();
        session.RenderedPages.AddRange(_renderedPages);
        session.GeneratedPageMarkdown.Clear();
        foreach (var kv in _generatedPageMarkdown)
        {
            session.GeneratedPageMarkdown[kv.Key] = kv.Value;
        }

        session.CurrentIndex = _currentRenderedPageIndex;
        session.IsLocalMode = _isLocalMarkdownMode;
        session.CurrentLocalPath = _currentLocalMarkdownPath;
        session.LocalFolderMode = _localFolderMode;
        session.LocalFolderFiles.Clear();
        foreach (var kv in _localFolderFiles)
        {
            session.LocalFolderFiles[kv.Key] = kv.Value;
        }
        session.LocalFolderOrder.Clear();
        session.LocalFolderOrder.AddRange(_localFolderOrder);
        session.CurrentPageHtml = CurrentPageHtml;
        session.CurrentPageMarkdown = CurrentPageMarkdown;
        session.CurrentDocumentTitle = CurrentDocumentTitle;
        session.PageNavigationText = PageNavigationText;
        session.CurrentPageLastUpdated = _currentPageLastUpdated;
    }

    private void RestoreSession(WorkspaceTabSession session)
    {
        _renderedPages.Clear();
        _renderedPages.AddRange(session.RenderedPages);
        _generatedPageMarkdown.Clear();
        foreach (var kv in session.GeneratedPageMarkdown)
        {
            _generatedPageMarkdown[kv.Key] = kv.Value;
        }

        _currentRenderedPageIndex = session.CurrentIndex;
        _isLocalMarkdownMode = session.IsLocalMode;
        _currentLocalMarkdownPath = session.CurrentLocalPath;
        _currentPageLastUpdated = session.CurrentPageLastUpdated;
        _localFolderMode = session.LocalFolderMode;
        _localFolderFiles.Clear();
        foreach (var kv in session.LocalFolderFiles)
        {
            _localFolderFiles[kv.Key] = kv.Value;
        }
        _localFolderOrder.Clear();
        _localFolderOrder.AddRange(session.LocalFolderOrder);

        CurrentDocumentTitle = session.CurrentDocumentTitle;
        PageNavigationText = session.PageNavigationText;
        CurrentPageMarkdown = session.CurrentPageMarkdown;
        // Setting Html last triggers the preview re-render via PropertyChanged.
        CurrentPageHtml = session.CurrentPageHtml;

        OnPropertyChanged(nameof(CurrentPageLastUpdatedDisplay));
        OnPropertyChanged(nameof(HasLoadedPage));
        RaiseNavigationStateChanged();
    }

    public async Task SelectLocalPageAsync(LocalPageNodeViewModel? node)
    {
        if (node is not { IsFile: true } || string.IsNullOrWhiteSpace(node.RenderedPath))
        {
            return;
        }

        var index = await EnsureLocalPageRenderedAsync(node.RenderedPath);
        if (index >= 0)
        {
            await LoadRenderedPageAsync(index);
        }
    }

    /// <summary>
    /// Returns the rendered-pages index for a local-folder relative path,
    /// rendering it on demand (and caching it) on first access. Returns the
    /// existing index for already-rendered pages (incl. single-file mode).
    /// </summary>
    private async Task<int> EnsureLocalPageRenderedAsync(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return -1;
        }

        var existing = _renderedPages.FindIndex(
            p => string.Equals(p.Path, relativePath, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            return existing;
        }

        if (!_localFolderFiles.TryGetValue(relativePath, out var absolutePath) || !File.Exists(absolutePath))
        {
            return -1;
        }

        var rendered = await _wikiPageRenderService.RenderLocalMarkdownAsync(absolutePath);
        if (rendered == null || string.IsNullOrWhiteSpace(rendered.HtmlFilePath))
        {
            return -1;
        }

        rendered.Path = relativePath;
        _generatedPageMarkdown[relativePath] = await File.ReadAllTextAsync(absolutePath);
        RegisterPathLastUpdated(relativePath, File.GetLastWriteTime(absolutePath));
        _renderedPages.Add(rendered);
        return _renderedPages.Count - 1;
    }

    /// <summary>
    /// Renders the checked local-folder pages not yet cached (used by "all
    /// pages" export). Only the files the user left checked are rendered.
    /// </summary>
    private async Task EnsureAllLocalFolderRenderedAsync()
    {
        if (!_localFolderMode)
        {
            return;
        }

        var checkedPaths = GetCheckedLocalPaths();
        var targets = _localFolderOrder.Where(checkedPaths.Contains).ToList();
        for (var i = 0; i < targets.Count; i++)
        {
            await EnsureLocalPageRenderedAsync(targets[i]);
            BusyMessage = string.Format(
                AppText.S("wpf.workspace.busy.loading_folder_progress", "Loading folder... {0}/{1}"),
                i + 1, targets.Count);
        }
    }

    /// <summary>
    /// Renders the checked pages of a multi-page local folder/zip and opens the
    /// first of them. Triggered by the "Load pages" button in the Local tab.
    /// </summary>
    public async Task LoadLocalPagesAsync()
    {
        if (!_localFolderMode || IsLoading)
        {
            return;
        }

        var checkedPaths = GetCheckedLocalPaths();
        var ordered = _localFolderOrder.Where(checkedPaths.Contains).ToList();
        if (ordered.Count == 0)
        {
            Status = AppText.S("wpf.workspace.status.no_checked_pages", "Select one or more pages in the tree.");
            return;
        }

        try
        {
            IsLoading = true;
            IsLoadingPages = true;
            BusyMessage = AppText.S("wpf.workspace.busy.loading_pages", "Loading pages...");

            foreach (var rel in ordered)
            {
                await EnsureLocalPageRenderedAsync(rel);
            }

            var firstIndex = _renderedPages.FindIndex(
                p => string.Equals(p.Path, ordered[0], StringComparison.OrdinalIgnoreCase));
            if (firstIndex >= 0)
            {
                await LoadRenderedPageAsync(firstIndex);
            }

            Status = string.Format(
                AppText.S("wpf.workspace.status.loaded_checked_pages", "{0} selected page(s) loaded"),
                ordered.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(
                AppText.S("wpf.workspace.status.error_loading_pages", "Error loading pages: {0}"),
                ex.Message);
        }
        finally
        {
            IsLoadingPages = false;
            BusyMessage = string.Empty;
            IsLoading = false;
        }
    }

    /// <summary>Relative paths of the checked file nodes in the local tree.</summary>
    private HashSet<string> GetCheckedLocalPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(IEnumerable<LocalPageNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsFile && node.IsChecked && !string.IsNullOrWhiteSpace(node.RenderedPath))
                {
                    set.Add(node.RenderedPath!);
                }

                Walk(node.Children);
            }
        }

        Walk(LocalPageTree);
        return set;
    }

    /// <summary>Snapshot of one tab's loaded/preview state (see SelectedTabIndex).</summary>
    private sealed class WorkspaceTabSession
    {
        public List<RenderedWikiPage> RenderedPages { get; } = [];
        public Dictionary<string, string> GeneratedPageMarkdown { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int CurrentIndex { get; set; } = -1;
        public bool IsLocalMode { get; set; }
        public string? CurrentLocalPath { get; set; }
        public string CurrentPageHtml { get; set; } = string.Empty;
        public string CurrentPageMarkdown { get; set; } = string.Empty;
        public string CurrentDocumentTitle { get; set; } = string.Empty;
        public string PageNavigationText { get; set; } = string.Empty;
        public DateTime? CurrentPageLastUpdated { get; set; }
        public bool LocalFolderMode { get; set; }
        public Dictionary<string, string> LocalFolderFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> LocalFolderOrder { get; } = [];
    }
}
