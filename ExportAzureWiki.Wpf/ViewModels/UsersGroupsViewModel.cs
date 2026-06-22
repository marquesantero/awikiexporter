using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Wpf.Commands;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class UsersGroupsViewModel : ViewModelBase
{
    private readonly IAdminCatalogService _service;
    private string _status = AppText.S("wpf.users.status.ready", "Ready");
    private bool _isLoading;
    private UserRecord? _selectedUser;
    private IdentityGroup? _selectedGroup;
    private IDictionary<int, int> _groupMemberCounts = new Dictionary<int, int>();

    public ObservableCollection<UserRecord> Users { get; } = [];
    public ObservableCollection<IdentityGroup> Groups { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand DeleteGroupCommand { get; }

    public UsersGroupsViewModel(IAdminCatalogService service)
    {
        _service = service;
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        DeleteUserCommand = new RelayCommand(async () => await DeleteUserAsync(), () => SelectedUser is { Id: > 0 });
        DeleteGroupCommand = new RelayCommand(async () => await DeleteGroupAsync(), () => SelectedGroup is { Id: > 0 });
    }

    public string Title => AppText.S("wpf.users.title", "Users and Groups");
    public string UsersSectionTitle => AppText.S("wpf.users.users.title", "Users");
    public string GroupsSectionTitle => AppText.S("wpf.users.groups.title", "Groups");
    public string RefreshText => AppText.S("common.refresh", "Refresh");
    public string NewText => AppText.S("common.new", "New");
    public string EditText => AppText.S("common.edit", "Edit");
    public string DeleteText => AppText.S("common.delete", "Delete");
    public string IdHeader => AppText.S("wpf.common.id", "Id");
    public string UsernameHeader => AppText.S("admin.user.field.username", "Username");
    public string EmailHeader => AppText.S("admin.user.field.email", "Email");
    public string DisplayNameHeader => AppText.S("admin.user.field.display_name", "Name");
    public string ActiveHeader => AppText.S("common.active", "Active");
    public string NameHeader => AppText.S("common.name", "Name");
    public string DescriptionHeader => AppText.S("admin.groups.field.description", "Description");
    public string SourceHeader => AppText.S("wpf.users.groups.source", "Source");
    public string SystemHeader => AppText.S("wpf.users.groups.system", "System");
    public string MembersText => AppText.S("wpf.users.groups.members", "Members:");

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

    public UserRecord? SelectedUser
    {
        get => _selectedUser;
        set
        {
            _selectedUser = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditSelectedUser));
            (DeleteUserCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public IdentityGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            _selectedGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditSelectedGroup));
            OnPropertyChanged(nameof(SelectedGroupMemberCount));
            (DeleteGroupCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool CanEditSelectedUser => SelectedUser != null;
    public bool CanEditSelectedGroup => SelectedGroup != null;

    public int SelectedGroupMemberCount
    {
        get
        {
            if (SelectedGroup == null)
            {
                return 0;
            }

            return _groupMemberCounts.TryGetValue(SelectedGroup.Id, out var count) ? count : 0;
        }
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        try
        {
            IsLoading = true;
            Status = AppText.S("wpf.users.status.loading", "Loading users and groups...");

            Users.Clear();
            Groups.Clear();

            var users = await _service.LoadUsersAsync();
            var groups = await _service.LoadGroupsAsync();
            _groupMemberCounts = await _service.LoadGroupMemberCountsAsync();

            foreach (var user in users) Users.Add(user);
            foreach (var group in groups) Groups.Add(group);

            SelectedUser = Users.FirstOrDefault();
            SelectedGroup = Groups.FirstOrDefault();

            Status = string.Format(
                AppText.S("wpf.users.status.loaded", "Loaded {0} user(s) and {1} group(s)"),
                Users.Count,
                Groups.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.users.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public UserRecord CreateUserDraftForNew()
    {
        Status = AppText.S("wpf.users.status.new_user", "New user draft created.");
        return new UserRecord
        {
            Username = string.Empty,
            Email = string.Empty,
            DisplayName = string.Empty,
            IsActive = true,
            AuthenticationMethod = AuthenticationMethod.Local
        };
    }

    public UserRecord? CreateUserDraftFromSelected()
    {
        if (SelectedUser == null)
        {
            return null;
        }

        var u = SelectedUser;
        return new UserRecord
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            DisplayName = u.DisplayName,
            PasswordHash = u.PasswordHash,
            PasswordSalt = u.PasswordSalt,
            IsActive = u.IsActive,
            AuthenticationMethod = u.AuthenticationMethod,
            ExternalId = u.ExternalId,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
            LastModifiedAt = u.LastModifiedAt
        };
    }

    public async Task<bool> SaveUserFromDialogAsync(UserRecord draft, string? password)
    {
        if (string.IsNullOrWhiteSpace(draft.Username))
        {
            Status = AppText.S("admin.user.validation.username_required", "Username is required.");
            return false;
        }

        try
        {
            var pwd = password;
            if (draft.Id <= 0 &&
                string.Equals(draft.AuthenticationMethod?.ToString(), AuthenticationMethod.Local.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(pwd))
            {
                pwd = "ChangeMe123!";
            }

            await _service.SaveUserAsync(draft, pwd);
            Status = AppText.S("wpf.users.status.saved_user", "User saved.");
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.users.status.error", "Error: {0}"), ex.Message);
            return false;
        }
    }

    private async Task DeleteUserAsync()
    {
        if (SelectedUser == null || SelectedUser.Id <= 0)
        {
            return;
        }

        var user = SelectedUser;
        var confirmation = MessageBox.Show(
            string.Format(
                AppText.S("wpf.confirm.delete_user.message", "Delete user '{0}'? This action cannot be undone."),
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName),
            AppText.S("wpf.confirm.delete_user.title", "Confirm Deletion"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            Status = AppText.S("wpf.confirm.delete_user.canceled", "User deletion canceled.");
            return;
        }

        try
        {
            if (await _service.DeleteUserAsync(user.Id))
            {
                Status = AppText.S("wpf.users.status.deleted_user", "User deleted.");
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.users.status.error", "Error: {0}"), ex.Message);
        }
    }

    public IdentityGroup CreateGroupDraftForNew()
    {
        Status = AppText.S("wpf.users.status.new_group", "New group draft created.");
        return new IdentityGroup
        {
            Name = string.Empty,
            Description = string.Empty,
            IsSystem = false,
            Source = "Local"
        };
    }

    public IdentityGroup? CreateGroupDraftFromSelected()
    {
        if (SelectedGroup == null)
        {
            return null;
        }

        var g = SelectedGroup;
        return new IdentityGroup
        {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            IsSystem = g.IsSystem,
            Source = g.Source,
            CreatedAt = g.CreatedAt
        };
    }

    public async Task<bool> SaveGroupFromDialogAsync(IdentityGroup draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            Status = AppText.S("wpf.users.status.validation_group", "Group name is required.");
            return false;
        }

        try
        {
            await _service.SaveGroupAsync(draft);
            Status = AppText.S("wpf.users.status.saved_group", "Group saved.");
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.users.status.error", "Error: {0}"), ex.Message);
            return false;
        }
    }

    public Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod method, string? searchTerm)
        => _service.SearchExternalUsersAsync(method, searchTerm);

    public Task<IReadOnlyList<ExternalDirectoryUser>> SearchExternalUsersAsync(AuthenticationMethod method, string? searchTerm, int? providerId)
        => _service.SearchExternalUsersAsync(method, searchTerm, providerId);

    public async Task<IReadOnlyList<OAuthProvider>> LoadExternalProvidersAsync(AuthenticationMethod method)
    {
        var providers = await _service.LoadOAuthProvidersAsync();
        IEnumerable<OAuthProvider> filtered = method switch
        {
            AuthenticationMethod.AzureAD => providers.Where(p =>
                p.IsEnabled &&
                (string.Equals(p.ProviderName, "AzureAD", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(p.ProviderName, "Microsoft", StringComparison.OrdinalIgnoreCase))),
            AuthenticationMethod.OAuth => providers.Where(p =>
                p.IsEnabled &&
                string.Equals(p.ProviderName, "GitHub", StringComparison.OrdinalIgnoreCase)),
            _ => []
        };

        return filtered
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task DeleteGroupAsync()
    {
        if (SelectedGroup == null || SelectedGroup.Id <= 0)
        {
            return;
        }

        var group = SelectedGroup;
        var confirmation = MessageBox.Show(
            string.Format(
                AppText.S("wpf.confirm.delete_group.message", "Delete group '{0}'? This action cannot be undone."),
                group.Name),
            AppText.S("wpf.confirm.delete_group.title", "Confirm Deletion"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            Status = AppText.S("wpf.confirm.delete_group.canceled", "Group deletion canceled.");
            return;
        }

        try
        {
            if (await _service.DeleteGroupAsync(group.Id))
            {
                Status = AppText.S("wpf.users.status.deleted_group", "Group deleted.");
                await LoadAsync();
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.users.status.error", "Error: {0}"), ex.Message);
        }
    }
}
