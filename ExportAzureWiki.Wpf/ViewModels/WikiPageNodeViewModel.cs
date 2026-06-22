using System.Collections.ObjectModel;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class WikiPageNodeViewModel : ViewModelBase
{
    public required string Title { get; init; }
    public required string Path { get; init; }
    public bool IsPage { get; set; }

    private bool _isChecked;
    private bool _isUpdatingCheck;

    public WikiPageNodeViewModel? Parent { get; set; }
    public ObservableCollection<WikiPageNodeViewModel> Children { get; } = [];

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

    public override string ToString() => Title;
}

