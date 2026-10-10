using System.Collections.ObjectModel;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Commands;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>
/// Management of users, roles and access rules. Passwords are not kept here: the view hands them over
/// only for the operation that needs them.
/// </summary>
public sealed class AccessViewModel : ViewModelBase
{
    private const string AllDevicesLabel = "Všetky zariadenia";

    private readonly IServerClient _server;
    private readonly IConfirmationDialog _confirmation;

    private IReadOnlyList<RoleInfo> _roleInfos = [];
    private UserRowViewModel? _selectedUser;
    private RoleRowViewModel? _selectedRole;
    private RuleRowViewModel? _selectedRule;
    private PermissionOption? _selectedPermission;
    private DeviceOption? _selectedDevice;
    private IReadOnlyList<SelectableRole> _selectedUserRoles = [];
    private IReadOnlyList<SelectableRole> _newUserRoles = [];
    private string _newUserName = string.Empty;
    private string _newRoleName = string.Empty;
    private string _message = string.Empty;

    public AccessViewModel(IServerClient server, IConfirmationDialog confirmation)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(confirmation);

        _server = server;
        _confirmation = confirmation;

        SaveUserRolesCommand = new AsyncRelayCommand(SaveUserRolesAsync, () => SelectedUser is not null);
        ToggleUserActiveCommand = new AsyncRelayCommand(ToggleUserActiveAsync, () => SelectedUser is not null);
        CreateRoleCommand = new AsyncRelayCommand(CreateRoleAsync, () => !string.IsNullOrWhiteSpace(NewRoleName));
        DeleteRoleCommand = new AsyncRelayCommand(DeleteRoleAsync, () => SelectedRole is { IsBuiltIn: false });
        AddRuleCommand = new AsyncRelayCommand(
            AddRuleAsync,
            () => SelectedRole is { IsBuiltIn: false } && SelectedPermission is not null && SelectedDevice is not null);
        RemoveRuleCommand = new AsyncRelayCommand(RemoveRuleAsync, () => SelectedRole is { IsBuiltIn: false } && SelectedRule is not null);
    }

    public ObservableCollection<UserRowViewModel> Users { get; } = [];

    public ObservableCollection<RoleRowViewModel> Roles { get; } = [];

    public ObservableCollection<PermissionOption> Permissions { get; } = [];

    /// <summary>What a rule can apply to: all devices, or one of the devices.</summary>
    public ObservableCollection<DeviceOption> Devices { get; } = [];

    public AsyncRelayCommand SaveUserRolesCommand { get; }

    public AsyncRelayCommand ToggleUserActiveCommand { get; }

    public AsyncRelayCommand CreateRoleCommand { get; }

    public AsyncRelayCommand DeleteRoleCommand { get; }

    public AsyncRelayCommand AddRuleCommand { get; }

    public AsyncRelayCommand RemoveRuleCommand { get; }

    public UserRowViewModel? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (!SetProperty(ref _selectedUser, value))
            {
                return;
            }

            SelectedUserRoles = BuildSelectableRoles(value?.RoleIds ?? []);
            OnPropertyChanged(nameof(ToggleUserActiveText));
            SaveUserRolesCommand.RaiseCanExecuteChanged();
            ToggleUserActiveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>All roles, ticked where the selected user has the role.</summary>
    public IReadOnlyList<SelectableRole> SelectedUserRoles
    {
        get => _selectedUserRoles;
        private set => SetProperty(ref _selectedUserRoles, value);
    }

    public string ToggleUserActiveText => SelectedUser is { IsActive: false } ? "Aktivovať" : "Deaktivovať";

    public string NewUserName
    {
        get => _newUserName;
        set => SetProperty(ref _newUserName, value);
    }

    /// <summary>All roles, ticked where the user being created should get the role.</summary>
    public IReadOnlyList<SelectableRole> NewUserRoles
    {
        get => _newUserRoles;
        private set => SetProperty(ref _newUserRoles, value);
    }

    public RoleRowViewModel? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (!SetProperty(ref _selectedRole, value))
            {
                return;
            }

            SelectedRule = null;
            DeleteRoleCommand.RaiseCanExecuteChanged();
            AddRuleCommand.RaiseCanExecuteChanged();
            RemoveRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public RuleRowViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                RemoveRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public PermissionOption? SelectedPermission
    {
        get => _selectedPermission;
        set
        {
            if (SetProperty(ref _selectedPermission, value))
            {
                AddRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public DeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                AddRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewRoleName
    {
        get => _newRoleName;
        set
        {
            if (SetProperty(ref _newRoleName, value))
            {
                CreateRoleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The result of the last operation, or why it failed.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Loads users, roles and devices from the server, keeping the selection where it still exists.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var roles = await _server.GetRolesAsync();
            var users = await _server.GetUsersAsync();
            var devices = await GetVisibleDevicesAsync();

            var selectedUserId = SelectedUser?.Id;
            var selectedRoleId = SelectedRole?.Id;
            var selectedPermission = SelectedPermission?.Name;
            var selectedDeviceId = SelectedDevice?.Id;

            _roleInfos = roles.Roles;

            Devices.Clear();
            Devices.Add(new DeviceOption(string.Empty, AllDevicesLabel));

            foreach (var device in devices)
            {
                Devices.Add(new DeviceOption(device.Id, device.Name));
            }

            Permissions.Clear();

            foreach (var permission in roles.Permissions)
            {
                Permissions.Add(new PermissionOption(permission, PermissionOption.Describe(permission)));
            }

            var deviceNames = devices.ToDictionary(device => device.Id, device => device.Name);

            Roles.Clear();

            foreach (var role in roles.Roles)
            {
                Roles.Add(new RoleRowViewModel(role, deviceNames));
            }

            var roleNames = roles.Roles.ToDictionary(role => role.Id, role => role.Name);

            Users.Clear();

            foreach (var user in users)
            {
                Users.Add(new UserRowViewModel(user, roleNames));
            }

            SelectedUser = Users.FirstOrDefault(user => user.Id == selectedUserId);
            SelectedRole = Roles.FirstOrDefault(role => role.Id == selectedRoleId);
            SelectedPermission = Permissions.FirstOrDefault(permission => permission.Name == selectedPermission) ?? Permissions.FirstOrDefault();
            SelectedDevice = Devices.FirstOrDefault(device => device.Id == selectedDeviceId) ?? Devices[0];

            // The ticks follow the roles as they are now; the same user or none may still be selected.
            SelectedUserRoles = BuildSelectableRoles(SelectedUser?.RoleIds ?? []);
            NewUserRoles = BuildSelectableRoles(NewUserRoles.Where(role => role.IsSelected).Select(role => role.Id).ToList());
        }
        catch (ServerClientException exception)
        {
            Message = Describe(exception);
        }
    }

    /// <summary>Someone who manages users need not be allowed to see devices; rules for all devices still work then.</summary>
    private async Task<IReadOnlyList<DeviceInfo>> GetVisibleDevicesAsync()
    {
        try
        {
            return await _server.GetDevicesAsync();
        }
        catch (ServerClientException exception) when (exception.Kind == ServerErrorKind.AccessDenied)
        {
            return [];
        }
    }

    /// <returns>True when the user was created.</returns>
    public async Task<bool> CreateUserAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(NewUserName) || string.IsNullOrEmpty(password))
        {
            Message = "Zadajte meno aj heslo nového používateľa.";

            return false;
        }

        var name = NewUserName.Trim();
        var roleIds = NewUserRoles.Where(role => role.IsSelected).Select(role => role.Id).ToList();

        return await RunAsync(
            async () =>
            {
                await _server.CreateUserAsync(name, password, roleIds);
                NewUserName = string.Empty;
                NewUserRoles = BuildSelectableRoles([]);
            },
            $"Používateľ „{name}“ bol vytvorený.");
    }

    /// <returns>True when the password was set.</returns>
    public async Task<bool> ResetPasswordAsync(string newPassword)
    {
        if (SelectedUser is not { } user)
        {
            return false;
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            Message = "Zadajte nové heslo.";

            return false;
        }

        return await RunAsync(
            () => _server.ResetUserPasswordAsync(user.Id, newPassword),
            $"Používateľ „{user.UserName}“ má nové heslo a bol všade odhlásený.");
    }

    private async Task SaveUserRolesAsync()
    {
        if (SelectedUser is not { } user)
        {
            return;
        }

        var roleIds = SelectedUserRoles.Where(role => role.IsSelected).Select(role => role.Id).ToList();

        await RunAsync(() => _server.SetUserRolesAsync(user.Id, roleIds), $"Roly používateľa „{user.UserName}“ boli uložené.");
    }

    private async Task ToggleUserActiveAsync()
    {
        if (SelectedUser is not { } user)
        {
            return;
        }

        var activate = !user.IsActive;

        if (!activate && !_confirmation.Confirm(
                "Deaktivovať používateľa",
                $"Naozaj chcete deaktivovať používateľa „{user.UserName}“? Nebude sa môcť prihlásiť a bude všade odhlásený."))
        {
            return;
        }

        await RunAsync(
            () => _server.SetUserActiveAsync(user.Id, activate),
            activate ? $"Používateľ „{user.UserName}“ bol aktivovaný." : $"Používateľ „{user.UserName}“ bol deaktivovaný.");
    }

    private async Task CreateRoleAsync()
    {
        var name = NewRoleName.Trim();

        await RunAsync(
            async () =>
            {
                await _server.CreateRoleAsync(name);
                NewRoleName = string.Empty;
            },
            $"Rola „{name}“ bola vytvorená.");
    }

    private async Task DeleteRoleAsync()
    {
        if (SelectedRole is not { } role)
        {
            return;
        }

        if (!_confirmation.Confirm(
                "Odstrániť rolu",
                $"Naozaj chcete odstrániť rolu „{role.Name}“? Používatelia, ktorí ju majú, o ňu prídu."))
        {
            return;
        }

        await RunAsync(() => _server.DeleteRoleAsync(role.Id), $"Rola „{role.Name}“ bola odstránená.");
    }

    private async Task AddRuleAsync()
    {
        if (SelectedRole is not { } role || SelectedPermission is not { } permission || SelectedDevice is not { } device)
        {
            return;
        }

        await RunAsync(
            () => _server.AddAccessRuleAsync(role.Id, permission.Name, device.Id),
            $"Rola „{role.Name}“ má pravidlo: {permission.Label} – {device.Label}.");
    }

    private async Task RemoveRuleAsync()
    {
        if (SelectedRole is not { } role || SelectedRule is not { } rule)
        {
            return;
        }

        await RunAsync(
            () => _server.RemoveAccessRuleAsync(role.Id, rule.Permission, rule.DeviceId),
            $"Pravidlo bolo z roly „{role.Name}“ odobraté.");
    }

    /// <summary>Runs an operation on the server, reloads the lists and reports the result.</summary>
    private async Task<bool> RunAsync(Func<Task> operation, string successMessage)
    {
        try
        {
            await operation();
        }
        catch (ServerClientException exception)
        {
            Message = Describe(exception);

            return false;
        }

        await LoadAsync();
        Message = successMessage;

        return true;
    }

    private IReadOnlyList<SelectableRole> BuildSelectableRoles(IReadOnlyCollection<string> selectedIds)
    {
        return _roleInfos.Select(role => new SelectableRole(role.Id, role.Name) { IsSelected = selectedIds.Contains(role.Id) }).ToList();
    }

    private static string Describe(ServerClientException exception)
    {
        return exception.Kind switch
        {
            ServerErrorKind.Unavailable => "Server nie je dostupný.",
            ServerErrorKind.AccessDenied => "Na túto operáciu nemáte oprávnenie.",
            ServerErrorKind.NotSignedIn => "Prihlásenie vypršalo. Zavrite okno a prihláste sa znova.",
            ServerErrorKind.NotFound => "Položka už neexistuje. Zoznam bol medzitým zmenený.",
            ServerErrorKind.InvalidInput => $"Server zmenu odmietol: {exception.Message}",
            _ => $"Chyba servera: {exception.Message}",
        };
    }
}

public sealed class UserRowViewModel
{
    public UserRowViewModel(UserInfo user, IReadOnlyDictionary<string, string> roleNames)
    {
        Id = user.Id;
        UserName = user.UserName;
        IsActive = user.IsActive;
        RoleIds = user.RoleIds.ToList();
        StateText = !user.IsActive ? "Deaktivovaný" : user.IsLocked ? "Zamknutý" : "Aktívny";
        RolesText = user.RoleIds.Count == 0
            ? "–"
            : string.Join(", ", user.RoleIds.Select(roleId => roleNames.GetValueOrDefault(roleId, "?")).Order());
    }

    public string Id { get; }

    public string UserName { get; }

    public bool IsActive { get; }

    public IReadOnlyList<string> RoleIds { get; }

    public string StateText { get; }

    public string RolesText { get; }
}

public sealed class RoleRowViewModel
{
    public RoleRowViewModel(RoleInfo role, IReadOnlyDictionary<string, string> deviceNames)
    {
        Id = role.Id;
        Name = role.Name;
        IsBuiltIn = role.IsBuiltIn;
        DisplayName = role.IsBuiltIn ? $"{role.Name} (vstavaná)" : role.Name;
        Rules = role.Rules
            .Select(rule => new RuleRowViewModel(rule, deviceNames))
            .OrderBy(rule => rule.PermissionText, StringComparer.CurrentCulture)
            .ThenBy(rule => rule.DeviceText, StringComparer.CurrentCulture)
            .ToList();
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsBuiltIn { get; }

    public string DisplayName { get; }

    public IReadOnlyList<RuleRowViewModel> Rules { get; }

    /// <summary>Screen readers announce an item of a list by this text.</summary>
    public override string ToString() => DisplayName;
}

public sealed class RuleRowViewModel
{
    public RuleRowViewModel(AccessRuleInfo rule, IReadOnlyDictionary<string, string> deviceNames)
    {
        Permission = rule.Permission;
        DeviceId = rule.DeviceId;
        PermissionText = PermissionOption.Describe(rule.Permission);
        DeviceText = string.IsNullOrEmpty(rule.DeviceId)
            ? "Všetky zariadenia"
            : deviceNames.GetValueOrDefault(rule.DeviceId, "Zariadenie, ktoré nevidíte");
    }

    public string Permission { get; }

    /// <summary>Empty when the rule applies to all devices.</summary>
    public string DeviceId { get; }

    public string PermissionText { get; }

    public string DeviceText { get; }
}

/// <summary>A role with a tick box next to it.</summary>
public sealed class SelectableRole(string id, string name) : ViewModelBase
{
    private bool _isSelected;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record PermissionOption(string Name, string Label)
{
    public static string Describe(string permission)
    {
        return permission switch
        {
            "ViewDevices" => "Zobrazenie zariadení",
            "ManageDevices" => "Správa zariadení",
            "ManageSettings" => "Správa nastavení",
            "ManageUsers" => "Správa používateľov",
            _ => permission,
        };
    }

    public override string ToString() => Label;
}

/// <param name="Id">Empty for all devices.</param>
public sealed record DeviceOption(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The form for changing one's own password.</summary>
public sealed class ChangePasswordViewModel : ViewModelBase
{
    private readonly IServerClient _server;
    private string _message = string.Empty;

    public ChangePasswordViewModel(IServerClient server)
    {
        ArgumentNullException.ThrowIfNull(server);

        _server = server;
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <returns>True when the password was changed; the user is then signed out everywhere.</returns>
    public async Task<bool> ChangeAsync(string currentPassword, string newPassword, string repeatedPassword)
    {
        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            Message = "Zadajte súčasné aj nové heslo.";

            return false;
        }

        if (newPassword != repeatedPassword)
        {
            Message = "Nové heslo a jeho zopakovanie sa nezhodujú.";

            return false;
        }

        try
        {
            await _server.ChangeOwnPasswordAsync(currentPassword, newPassword);
            Message = string.Empty;

            return true;
        }
        catch (ServerClientException exception)
        {
            Message = exception.Kind switch
            {
                ServerErrorKind.Unavailable => "Server nie je dostupný.",
                ServerErrorKind.NotSignedIn => "Prihlásenie vypršalo. Prihláste sa znova.",
                ServerErrorKind.InvalidInput => $"Heslo sa nezmenilo: {exception.Message}",
                _ => $"Chyba servera: {exception.Message}",
            };

            return false;
        }
    }
}
