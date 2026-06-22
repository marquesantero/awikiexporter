using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Core.Models;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class GroupEditDialog : Window
{
    private readonly GroupEditModel _model;

    public GroupEditDialog(IdentityGroup source, bool isNew)
    {
        InitializeComponent();
        _model = new GroupEditModel(source, isNew);
        DataContext = _model;
    }

    public IdentityGroup Result => _model.ToIdentityGroup();

    private void BtnSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_model.Name))
        {
            MessageBox.Show(
                AppText.S("wpf.users.status.validation_group", "Group name is required."),
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
}

public sealed class GroupEditModel : INotifyPropertyChanged
{
    private readonly int _id;
    private readonly DateTime _createdAt;
    private string _name;
    private string _description;
    private string _source;
    private bool _isSystem;

    public GroupEditModel(IdentityGroup source, bool isNew)
    {
        _id = source.Id;
        _createdAt = source.CreatedAt;
        _name = source.Name;
        _description = source.Description ?? string.Empty;
        _source = source.Source ?? string.Empty;
        _isSystem = source.IsSystem;
        DialogTitle = isNew
            ? AppText.S("wpf.groups.dialog.new.title", "New Group")
            : AppText.S("wpf.groups.dialog.edit.title", "Edit Group");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string DialogTitle { get; }
    public string NameText => AppText.S("common.name", "Name");
    public string DescriptionText => AppText.S("admin.groups.field.description", "Description");
    public string SourceText => AppText.S("wpf.users.groups.source", "Source");
    public string IsSystemText => AppText.S("wpf.users.groups.system", "System");
    public string SaveText => AppText.S("common.save", "Save");
    public string CancelText => AppText.S("common.cancel", "Cancel");

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    public string Source
    {
        get => _source;
        set => Set(ref _source, value);
    }

    public bool IsSystem
    {
        get => _isSystem;
        set => Set(ref _isSystem, value);
    }

    public IdentityGroup ToIdentityGroup()
    {
        return new IdentityGroup
        {
            Id = _id,
            Name = Name?.Trim() ?? string.Empty,
            Description = Description?.Trim(),
            Source = Source?.Trim(),
            IsSystem = IsSystem,
            CreatedAt = _createdAt == default ? DateTime.Now : _createdAt
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
