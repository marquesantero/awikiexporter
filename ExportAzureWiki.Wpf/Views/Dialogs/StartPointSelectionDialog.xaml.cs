using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ExportAzureWiki.Core.Localization;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class StartPointSelectionDialog : Window
{
    private readonly StartPointSelectionDialogViewModel _model;

    public StartPointSelectionDialog(IEnumerable<string> availableStartPoints, HashSet<string> selectedStartPoints)
    {
        InitializeComponent();
        _model = new StartPointSelectionDialogViewModel(availableStartPoints, selectedStartPoints);
        DataContext = _model;
    }

    public IReadOnlyList<string> SelectedStartPoints =>
        ReduceToMinimalStartPoints(
            Flatten(_model.RootNodes)
            .Where(i => i.IsSelected)
            .Select(i => i.FullPath));

    private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnOk_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private static IEnumerable<StartPointSelectionTreeNode> Flatten(IEnumerable<StartPointSelectionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IReadOnlyList<string> ReduceToMinimalStartPoints(IEnumerable<string> startPoints)
    {
        var normalized = startPoints
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<string>();
        foreach (var candidate in normalized)
        {
            if (result.Any(parent =>
                    string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
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

public sealed class StartPointSelectionDialogViewModel
{
    public StartPointSelectionDialogViewModel(IEnumerable<string> availableStartPoints, HashSet<string> selectedStartPoints)
    {
        RootNodes = BuildTree(availableStartPoints, selectedStartPoints);
    }

    public ObservableCollection<StartPointSelectionTreeNode> RootNodes { get; }
    public string DialogTitle => AppText.S("permissions.new.start_points.dialog.title", "Select start points");
    public string OkText => AppText.S("common.ok", "OK");
    public string CancelText => AppText.S("common.cancel", "Cancel");

    private static ObservableCollection<StartPointSelectionTreeNode> BuildTree(IEnumerable<string> paths, HashSet<string> selectedPaths)
    {
        var roots = new ObservableCollection<StartPointSelectionTreeNode>();
        var map = new Dictionary<string, StartPointSelectionTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Select(NormalizePath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var currentPath = string.Empty;
            StartPointSelectionTreeNode? parent = null;
            foreach (var segment in segments)
            {
                currentPath += "/" + segment;
                if (!map.TryGetValue(currentPath, out var node))
                {
                    node = new StartPointSelectionTreeNode
                    {
                        Title = segment,
                        FullPath = currentPath,
                        Parent = parent
                    };

                    map[currentPath] = node;
                    if (parent == null)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }

                parent = node;
            }
        }

        foreach (var node in Flatten(roots))
        {
            node.SetSelectedSilently(selectedPaths.Contains(node.FullPath));
        }

        // Expand exact selections to descendants and then synchronize parent state.
        foreach (var node in Flatten(roots).Where(n => n.IsSelected))
        {
            node.ApplyToDescendants(true);
        }

        foreach (var node in Flatten(roots).OrderByDescending(n => n.FullPath.Count(c => c == '/')))
        {
            node.RefreshFromChildren();
        }

        return roots;
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

    private static IEnumerable<StartPointSelectionTreeNode> Flatten(IEnumerable<StartPointSelectionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}

public sealed class StartPointSelectionTreeNode : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isUpdatingSelection;
    public string Title { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public StartPointSelectionTreeNode? Parent { get; set; }
    public ObservableCollection<StartPointSelectionTreeNode> Children { get; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));

            if (_isUpdatingSelection)
            {
                return;
            }

            _isUpdatingSelection = true;
            try
            {
                ApplyToDescendants(value);
                Parent?.RefreshFromChildrenRecursive();
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }
    }

    public void SetSelectedSilently(bool value)
    {
        _isSelected = value;
    }

    public void ApplyToDescendants(bool value)
    {
        foreach (var child in Children)
        {
            child.SetSelectionFromParent(value);
        }
    }

    private void SetSelectionFromParent(bool value)
    {
        _isUpdatingSelection = true;
        try
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }

            foreach (var child in Children)
            {
                child.SetSelectionFromParent(value);
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    public void RefreshFromChildren()
    {
        if (Children.Count == 0)
        {
            return;
        }

        var allSelected = Children.All(c => c.IsSelected);
        if (_isSelected != allSelected)
        {
            _isSelected = allSelected;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    private void RefreshFromChildrenRecursive()
    {
        RefreshFromChildren();
        Parent?.RefreshFromChildrenRecursive();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
