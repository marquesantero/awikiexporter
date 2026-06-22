using System.Collections.ObjectModel;

namespace ExportAzureWiki.Wpf.ViewModels;

/// <summary>
/// A node in the "Local" workspace tab tree. Folder nodes group children;
/// file nodes carry a <see cref="RenderedPath"/> that matches the Path of an
/// entry in the rendered-pages list, so selecting one navigates to it.
/// </summary>
public sealed class LocalPageNodeViewModel : ViewModelBase
{
    public required string Title { get; init; }

    /// <summary>Rendered page path for file nodes; null for folder nodes.</summary>
    public string? RenderedPath { get; init; }

    public bool IsFile => RenderedPath != null;

    public LocalPageNodeViewModel? Parent { get; set; }
    public ObservableCollection<LocalPageNodeViewModel> Children { get; } = [];

    private bool _isChecked = true;
    private bool _isUpdatingCheck;

    /// <summary>
    /// Whether this node is selected for bulk actions (e.g. "all pages" export).
    /// Checking a folder checks its descendants. Defaults to checked so opening
    /// a folder selects everything, like before.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            OnPropertyChanged();

            if (_isUpdatingCheck)
            {
                return;
            }

            _isUpdatingCheck = true;
            try
            {
                foreach (var child in Children)
                {
                    child.IsChecked = value;
                }
            }
            finally
            {
                _isUpdatingCheck = false;
            }
        }
    }

    private bool _isVisible = true;

    /// <summary>Controls TreeViewItem visibility when a filter is applied.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public override string ToString() => Title;
}
