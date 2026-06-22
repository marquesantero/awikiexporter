using System.ComponentModel;
using System.Runtime.CompilerServices;
using ExportAzureWiki.Localization;

namespace ExportAzureWiki.Wpf.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    protected ViewModelBase()
    {
        LocalizationManager.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }
}

