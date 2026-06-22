using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Wpf.ViewModels;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class WikiEditDialog : Window
{
    private readonly WikiEditModel _model;
    private readonly WikiManagementViewModel _wikiManagementViewModel;
    private IReadOnlyList<string> _availableStartPoints = [];

    public WikiEditDialog(WikiConfiguration source, WikiManagementViewModel wikiManagementViewModel, bool isNew)
    {
        InitializeComponent();
        _wikiManagementViewModel = wikiManagementViewModel;
        _model = new WikiEditModel(source, isNew);
        _model.RebuildDynamicFields();
        DataContext = _model;
    }

    public WikiConfiguration Result => _model.ToWikiConfiguration();

    private void BtnSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_model.Name))
        {
            MessageBox.Show(
                AppText.S("wpf.wikis.status.validation_name", "Name is required."),
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

    private async void BtnTestConnection_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _model.CanSelectStartPoints = false;
            _availableStartPoints = await _wikiManagementViewModel.LoadWikiPathsForStartPointsAsync(_model.ToWikiConfiguration());
            _model.CanSelectStartPoints = _availableStartPoints.Count > 0;

            var message = _model.CanSelectStartPoints
                ? AppText.S("wpf.wikis.start_points.test_ok", "Connection succeeded. Start points are available to select.")
                : AppText.S("wpf.wikis.start_points.empty", "No pages were found to select start points.");

            MessageBox.Show(
                message,
                AppText.S("wiki.management.test.title", "Connection Test"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _model.CanSelectStartPoints = false;
            MessageBox.Show(
                string.Format(AppText.S("wpf.wikis.start_points.error", "Error loading start points: {0}"), ex.Message),
                AppText.S("common.error", "Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BtnSelectStartPoints_OnClick(object sender, RoutedEventArgs e)
    {
        if (_availableStartPoints.Count == 0)
        {
            MessageBox.Show(
                AppText.S("wpf.wikis.start_points.empty", "No pages were found to select start points."),
                AppText.S("common.information", "Information"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selected = ParseConfiguredStartPoints(_model.RootPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var picker = new StartPointSelectionDialog(_availableStartPoints, selected)
        {
            Owner = this
        };

        if (picker.ShowDialog() == true)
        {
            _model.RootPath = string.Join("|", picker.SelectedStartPoints);
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

public sealed class WikiEditModel : INotifyPropertyChanged
{
    private readonly string _id;
    private readonly DateTime _createdAt;
    private readonly string _ownerUserId;
    private readonly string _ownerDisplayName;
    private readonly bool _createdByAdmin;
    private readonly string _iconColor;
    private readonly WikiVisibilityScope _visibilityScope;
    private readonly bool _isNew;
    private string _name = string.Empty;
    private WikiPlatform _platform;
    private AuthenticationType _authType;
    private string _organizationUrl = string.Empty;
    private string _rootPath = string.Empty;
    private bool _isActive = true;
    private bool _isDefault;
    private bool _canSelectStartPoints;

    public WikiEditModel(WikiConfiguration source, bool isNew)
    {
        _id = source.Id;
        _createdAt = source.CreatedAt;
        _ownerUserId = source.OwnerUserId;
        _ownerDisplayName = source.OwnerDisplayName;
        _createdByAdmin = source.CreatedByAdmin;
        _iconColor = source.IconColor;
        _visibilityScope = source.VisibilityScope;
        _isNew = isNew;

        _name = source.Name;
        _platform = source.Platform;
        _authType = source.AuthType;
        _organizationUrl = source.OrganizationUrl;
        _rootPath = source.RootPath;
        _isActive = source.IsActive;
        _isDefault = source.IsDefault;

        ExistingAuthData = source.AuthenticationData.ToDictionary(k => k.Key, v => v.Value);
        ExistingPlatformData = source.PlatformSpecificData.ToDictionary(k => k.Key, v => v.Value);
        DialogTitle = isNew
            ? AppText.S("wpf.wikis.dialog.new.title", "New Wiki Configuration")
            : AppText.S("wpf.wikis.dialog.edit.title", "Edit Wiki Configuration");

        RefreshConfiguredStartPoints();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Bitbucket is wired through the model and switch branches but its connector
    // is still a stub (BaseWikiService returns empty/false), so it is hidden from
    // the selector until implemented -- a user must not be able to pick a source
    // that silently produces an empty wiki. The enum value is kept so stored
    // configs and switch statements remain valid.
    public ObservableCollection<WikiPlatform> Platforms { get; } =
        new(Enum.GetValues<WikiPlatform>().Where(p => p != WikiPlatform.Bitbucket));
    public ObservableCollection<AuthenticationType> AuthTypes { get; } = new(Enum.GetValues<AuthenticationType>());
    public ObservableCollection<DynamicFieldRow> DynamicFields { get; } = [];
    public Dictionary<string, string> ExistingAuthData { get; }
    public Dictionary<string, string> ExistingPlatformData { get; }
    public string DialogTitle { get; }

    public string NameText => AppText.S("common.name", "Name");
    public string PlatformText => AppText.S("wpf.wikis.platform", "Platform");
    public string AuthTypeText => AppText.S("wpf.wikis.auth_type", "Authentication");
    public string BaseUrlText => AppText.S("wpf.wikis.organization_url", "Organization URL");
    public string StartPointsText => AppText.S("wpf.wikis.start_points", "Start Points (| separated)");
    public string TestConnectionText => AppText.S("wiki.management.action.test", "Test Connection");
    public string SelectStartPointsText => AppText.S("wpf.common.pages", "Pages");
    public string DynamicFieldsText => AppText.S("wpf.wikis.dynamic_fields", "Platform/Auth fields");
    public string ActiveText => AppText.S("common.active", "Active");
    public string DefaultText => AppText.S("common.default", "Default");
    public string SaveText => AppText.S("common.save", "Save");
    public string CancelText => AppText.S("common.cancel", "Cancel");

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public WikiPlatform Platform
    {
        get => _platform;
        set
        {
            if (Set(ref _platform, value))
            {
                RebuildDynamicFields();
            }
        }
    }

    public AuthenticationType AuthType
    {
        get => _authType;
        set
        {
            if (Set(ref _authType, value))
            {
                RebuildDynamicFields();
            }
        }
    }

    public string OrganizationUrl
    {
        get => _organizationUrl;
        set => Set(ref _organizationUrl, value);
    }

    public string RootPath
    {
        get => _rootPath;
        set
        {
            if (Set(ref _rootPath, value))
            {
                RefreshConfiguredStartPoints();
            }
        }
    }

    /// <summary>
    /// The configured start points parsed from <see cref="RootPath"/>, shown in
    /// the dialog as a read-only checked list (mirrors the selection dialog).
    /// </summary>
    public ObservableCollection<string> ConfiguredStartPoints { get; } = [];

    private void RefreshConfiguredStartPoints()
    {
        ConfiguredStartPoints.Clear();
        if (string.IsNullOrWhiteSpace(_rootPath))
        {
            return;
        }

        var points = _rootPath
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var point in points)
        {
            ConfiguredStartPoints.Add(point);
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set => Set(ref _isDefault, value);
    }

    public bool CanSelectStartPoints
    {
        get => _canSelectStartPoints;
        set => Set(ref _canSelectStartPoints, value);
    }

    public void RebuildDynamicFields()
    {
        var currentValues = DynamicFields.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);
        DynamicFields.Clear();

        var fields = GetFieldsForSelection(Platform, AuthType);
        foreach (var field in fields)
        {
            var value = string.Empty;
            if (currentValues.TryGetValue(field.Key, out var editedValue))
            {
                value = editedValue;
            }
            else if (ExistingPlatformData.TryGetValue(field.Key, out var platformValue))
            {
                value = platformValue;
            }
            else if (ExistingAuthData.TryGetValue(field.Key, out var authValue))
            {
                value = authValue;
            }
            else if (DefaultFieldValue(field.Key) is { } defaultValue)
            {
                value = defaultValue;
            }

            var row = new DynamicFieldRow
            {
                Key = field.Key,
                Label = GetFieldDisplayName(field.Key),
                Value = value,
                IsSensitive = field.Value
            };

            foreach (var (optionValue, optionLabel) in GetFieldChoices(field.Key))
            {
                row.Options.Add(new FieldOption(row, optionValue, optionLabel));
            }

            // Choice fields always carry a concrete selection; default to the
            // first option when nothing was loaded/edited.
            if (row.IsChoice && string.IsNullOrWhiteSpace(row.Value))
            {
                row.Value = row.Options[0].Value;
            }

            DynamicFields.Add(row);
        }
    }

    public WikiConfiguration ToWikiConfiguration()
    {
        var cfg = new WikiConfiguration
        {
            Id = _id,
            CreatedAt = _createdAt == default ? DateTime.Now : _createdAt,
            Name = Name?.Trim() ?? string.Empty,
            Platform = Platform,
            AuthType = AuthType,
            BaseUrl = OrganizationUrl?.Trim() ?? string.Empty,
            RootPath = RootPath?.Trim() ?? string.Empty,
            IsActive = IsActive,
            IsDefault = IsDefault,
            LastUsedAt = DateTime.Now,
            OwnerUserId = _ownerUserId,
            OwnerDisplayName = _ownerDisplayName,
            CreatedByAdmin = _createdByAdmin,
            IconColor = _iconColor,
            VisibilityScope = _visibilityScope
        };

        foreach (var field in DynamicFields)
        {
            var value = field.Value?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (IsAuthenticationField(field.Key))
            {
                cfg.AuthenticationData[field.Key] = value;
            }
            else
            {
                cfg.PlatformSpecificData[field.Key] = value;
            }
        }

        return cfg;
    }

    private static Dictionary<string, bool> GetFieldsForSelection(WikiPlatform platform, AuthenticationType authType)
    {
        var fields = GetPlatformFields(platform);
        foreach (var authField in GetAuthenticationFields(platform, authType))
        {
            fields[authField.Key] = authField.Value;
        }

        return fields;
    }

    private static Dictionary<string, bool> GetPlatformFields(WikiPlatform platform)
    {
        return platform switch
        {
            WikiPlatform.AzureDevOps => new Dictionary<string, bool> { ["ProjectName"] = false, ["WikiName"] = false, ["RepositoryId"] = false },
            WikiPlatform.GitHub => new Dictionary<string, bool>
            {
                ["Owner"] = false,
                ["Repository"] = false,
                ["Mode"] = false,
                ["Branch"] = false,
                ["DocsPath"] = false
            },
            WikiPlatform.GitLab => new Dictionary<string, bool>
            {
                ["ProjectId"] = false,
                ["Mode"] = false,
                ["Branch"] = false,
                ["DocsPath"] = false
            },
            WikiPlatform.Bitbucket => new Dictionary<string, bool> { ["Workspace"] = false, ["Repository"] = false },
            _ => new Dictionary<string, bool>()
        };
    }

    private static Dictionary<string, bool> GetAuthenticationFields(WikiPlatform platform, AuthenticationType authType)
    {
        return authType switch
        {
            AuthenticationType.PersonalAccessToken => platform switch
            {
                WikiPlatform.Bitbucket => new Dictionary<string, bool> { ["AppPassword"] = true },
                _ => new Dictionary<string, bool> { ["Token"] = true }
            },
            AuthenticationType.BasicAuth => new Dictionary<string, bool> { ["Username"] = false, ["Password"] = true },
            AuthenticationType.ApiKey => new Dictionary<string, bool> { ["ApiKey"] = true },
            AuthenticationType.OAuth => new Dictionary<string, bool>
            {
                ["ClientId"] = false,
                ["ClientSecret"] = true,
                ["TenantId"] = false,
                ["Scopes"] = false,
                ["RedirectUri"] = false
            },
            _ => new Dictionary<string, bool>()
        };
    }

    /// <summary>
    /// Pre-filled value for a freshly created configuration. Only applied when
    /// the field has no loaded/edited value, and only for new wikis so editing
    /// an existing one never silently re-introduces a default the user removed.
    /// </summary>
    private string? DefaultFieldValue(string fieldName)
    {
        if (!_isNew)
        {
            return null;
        }

        return fieldName switch
        {
            "DocsPath" => "docs",
            _ => null
        };
    }

    /// <summary>
    /// Enumerated choices for a field, rendered as radio buttons. An empty list
    /// means the field is free text. Tuple is (stored value, display label).
    /// </summary>
    private static IReadOnlyList<(string Value, string Label)> GetFieldChoices(string fieldName)
    {
        return fieldName switch
        {
            "Mode" =>
            [
                ("Repo", AppText.S("wiki.management.dynamic.mode.repo", "Repository")),
                ("Wiki", AppText.S("wiki.management.dynamic.mode.wiki", "Wiki"))
            ],
            _ => []
        };
    }

    private static string GetFieldDisplayName(string fieldName)
    {
        return fieldName switch
        {
            "ProjectName" => AppText.S("wiki.management.dynamic.project_name", "Project"),
            "WikiName" => AppText.S("wiki.management.dynamic.wiki_name", "Wiki"),
            "RepositoryId" => AppText.S("wiki.management.dynamic.repository_id", "Repository"),
            "Owner" => AppText.S("wiki.management.dynamic.owner", "Owner"),
            "Repository" => AppText.S("wiki.management.dynamic.repository", "Repository"),
            "Mode" => AppText.S("wiki.management.dynamic.mode", "Source Mode (Repo or Wiki)"),
            "Branch" => AppText.S("wiki.management.dynamic.branch", "Branch"),
            "DocsPath" => AppText.S("wiki.management.dynamic.docs_path", "Docs Path"),
            "ProjectId" => AppText.S("wiki.management.dynamic.project_id", "Project ID"),
            "Workspace" => AppText.S("wiki.management.dynamic.workspace", "Workspace"),
            "SpaceKey" => AppText.S("wiki.management.dynamic.space_key", "Space Key"),
            "Token" => AppText.S("wiki.management.dynamic.token", "Token"),
            "ApiToken" => AppText.S("wiki.management.dynamic.api_token", "API Token"),
            "AppPassword" => AppText.S("wiki.management.dynamic.app_password", "App Password"),
            "Username" => AppText.S("wiki.management.dynamic.username", "Username"),
            "Password" => AppText.S("wiki.management.dynamic.password", "Password"),
            "ApiKey" => AppText.S("wiki.management.dynamic.api_key", "API Key"),
            "ClientId" => AppText.S("wiki.management.dynamic.client_id", "Client ID"),
            "ClientSecret" => AppText.S("wiki.management.dynamic.client_secret", "Client Secret"),
            "TenantId" => AppText.S("wiki.management.dynamic.tenant_id", "Tenant ID"),
            "Scopes" => AppText.S("wiki.management.dynamic.scopes", "Scopes"),
            "RedirectUri" => AppText.S("wiki.management.dynamic.redirect_uri", "Redirect URI"),
            _ => fieldName
        };
    }

    private static bool IsAuthenticationField(string fieldName)
    {
        return fieldName is "Token" or "ApiToken" or "AppPassword" or "Username" or "Password" or "ApiKey" or
               "ClientId" or "ClientSecret" or "TenantId" or "Scopes" or "RedirectUri";
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

public sealed class DynamicFieldRow : INotifyPropertyChanged
{
    private string _value = string.Empty;

    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSensitive { get; set; }

    /// <summary>Radio options; empty means this is a free-text field.</summary>
    public ObservableCollection<FieldOption> Options { get; } = [];

    public bool IsChoice => Options.Count > 0;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            // Keep radio buttons in sync when the value changes from elsewhere.
            foreach (var option in Options)
            {
                option.NotifySelectionChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// A single radio option of a <see cref="DynamicFieldRow"/>. Selecting it
/// writes its <see cref="Value"/> back to the owning row.
/// </summary>
public sealed class FieldOption : INotifyPropertyChanged
{
    private readonly DynamicFieldRow _row;

    public FieldOption(DynamicFieldRow row, string value, string label)
    {
        _row = row;
        Value = value;
        Label = label;
    }

    public string Value { get; }
    public string Label { get; }

    /// <summary>RadioButtons in the same row share a group via the row key.</summary>
    public string GroupName => _row.Key;

    public bool IsSelected
    {
        get => string.Equals(_row.Value, Value, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                _row.Value = Value;
            }
        }
    }

    internal void NotifySelectionChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
