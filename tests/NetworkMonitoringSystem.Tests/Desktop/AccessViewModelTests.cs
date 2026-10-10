namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public class AccessViewModelTests
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();
    private readonly Confirmation _confirmation = new();
    private readonly AccessViewModel _viewModel;

    public AccessViewModelTests()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        _server.Roles.Add(new RoleInfo
        {
            Id = "role-1",
            Name = "Administrator",
            IsBuiltIn = true,
            Rules = { new AccessRuleInfo { Permission = "ManageUsers" } },
        });
        _server.Roles.Add(new RoleInfo
        {
            Id = "role-2",
            Name = "Operator",
            Rules = { new AccessRuleInfo { Permission = "ViewDevices", DeviceId = "device-1" } },
        });
        _server.Users.Add(new UserInfo { Id = "user-1", UserName = "admin", IsActive = true, RoleIds = { "role-1" } });
        _server.Users.Add(new UserInfo { Id = "user-2", UserName = "jana", IsActive = false, RoleIds = { "role-2", "role-1" } });

        _viewModel = new AccessViewModel(_server, _confirmation);
    }

    [Fact]
    public async Task Load_ShowsUsersRolesRulesAndChoices()
    {
        await _viewModel.LoadAsync();

        Assert.Equal(["admin", "jana"], _viewModel.Users.Select(user => user.UserName));
        Assert.Equal(("Aktívny", "Administrator"), (_viewModel.Users[0].StateText, _viewModel.Users[0].RolesText));
        Assert.Equal(("Deaktivovaný", "Administrator, Operator"), (_viewModel.Users[1].StateText, _viewModel.Users[1].RolesText));

        Assert.Equal(["Administrator (vstavaná)", "Operator"], _viewModel.Roles.Select(role => role.DisplayName));
        var rule = Assert.Single(_viewModel.Roles[1].Rules);
        Assert.Equal(("Zobrazenie zariadení", "Router"), (rule.PermissionText, rule.DeviceText));
        Assert.Equal("Všetky zariadenia", _viewModel.Roles[0].Rules[0].DeviceText);

        Assert.Equal(["Všetky zariadenia", "Router"], _viewModel.Devices.Select(device => device.Label));
        Assert.Equal(4, _viewModel.Permissions.Count);
        Assert.Equal("Všetky zariadenia", _viewModel.SelectedDevice!.Label);
    }

    [Fact]
    public async Task CreateUser_SendsTrimmedNameAndTickedRoles_ClearsTheForm_AndShowsTheUser()
    {
        await _viewModel.LoadAsync();
        _viewModel.NewUserName = "  peter ";
        _viewModel.NewUserRoles.Single(role => role.Name == "Operator").IsSelected = true;

        Assert.True(await _viewModel.CreateUserAsync("long-enough-password"));

        var created = _server.Users.Single(user => user.UserName == "peter");
        Assert.Equal(["role-2"], created.RoleIds);
        Assert.Equal("long-enough-password", _server.Passwords["peter"]);
        Assert.Equal(string.Empty, _viewModel.NewUserName);
        Assert.DoesNotContain(_viewModel.NewUserRoles, role => role.IsSelected);
        Assert.Contains(_viewModel.Users, user => user.UserName == "peter");
        Assert.Contains("bol vytvorený", _viewModel.Message);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("peter", "")]
    public async Task CreateUser_WithoutNameOrPassword_DoesNotCallTheServer(string name, string password)
    {
        await _viewModel.LoadAsync();
        _viewModel.NewUserName = name;

        Assert.False(await _viewModel.CreateUserAsync(password));

        Assert.Equal(2, _server.Users.Count);
    }

    [Fact]
    public async Task CreateUser_RefusedByTheServer_ShowsTheReason_AndKeepsTheForm()
    {
        await _viewModel.LoadAsync();
        _viewModel.NewUserName = "admin";
        _server.Failure = new ServerClientException(ServerErrorKind.InvalidInput, "A user named 'admin' already exists.");

        Assert.False(await _viewModel.CreateUserAsync("long-enough-password"));

        Assert.Contains("already exists", _viewModel.Message);
        Assert.Equal("admin", _viewModel.NewUserName);
    }

    [Fact]
    public async Task SelectingUser_TicksTheirRoles_AndSavingSendsTheChangedSet()
    {
        await _viewModel.LoadAsync();
        _viewModel.SelectedUser = _viewModel.Users[0];
        Assert.Equal(["Administrator"], _viewModel.SelectedUserRoles.Where(role => role.IsSelected).Select(role => role.Name));

        _viewModel.SelectedUserRoles.Single(role => role.Name == "Operator").IsSelected = true;
        await _viewModel.SaveUserRolesCommand.ExecuteAsync();

        Assert.Equal(["role-1", "role-2"], _server.Users[0].RoleIds);

        // The same user stays selected after the lists are reloaded.
        Assert.Equal("admin", _viewModel.SelectedUser!.UserName);
        Assert.Equal(2, _viewModel.SelectedUserRoles.Count(role => role.IsSelected));
    }

    [Fact]
    public async Task ToggleActive_ActivatesWithoutAsking_AndDeactivatesOnlyAfterConfirmation()
    {
        await _viewModel.LoadAsync();

        _viewModel.SelectedUser = _viewModel.Users[1];
        Assert.Equal("Aktivovať", _viewModel.ToggleUserActiveText);
        await _viewModel.ToggleUserActiveCommand.ExecuteAsync();
        Assert.True(_server.Users[1].IsActive);
        Assert.Empty(_confirmation.Questions);
        Assert.Equal("Deaktivovať", _viewModel.ToggleUserActiveText);

        _confirmation.Answer = false;
        await _viewModel.ToggleUserActiveCommand.ExecuteAsync();
        Assert.True(_server.Users[1].IsActive);

        _confirmation.Answer = true;
        await _viewModel.ToggleUserActiveCommand.ExecuteAsync();
        Assert.False(_server.Users[1].IsActive);
        Assert.Equal(2, _confirmation.Questions.Count);
    }

    [Fact]
    public async Task ResetPassword_SendsNewPasswordOfSelectedUser()
    {
        await _viewModel.LoadAsync();
        Assert.False(await _viewModel.ResetPasswordAsync("new-password-1"));

        _viewModel.SelectedUser = _viewModel.Users[1];
        Assert.False(await _viewModel.ResetPasswordAsync(string.Empty));
        Assert.True(await _viewModel.ResetPasswordAsync("new-password-1"));

        Assert.Equal("new-password-1", _server.Passwords["user-2"]);
    }

    [Fact]
    public async Task Roles_CanBeCreatedAndDeleted_ButNotTheBuiltInOne()
    {
        await _viewModel.LoadAsync();
        Assert.False(_viewModel.CreateRoleCommand.CanExecute(null));

        _viewModel.NewRoleName = " Auditor ";
        await _viewModel.CreateRoleCommand.ExecuteAsync();
        Assert.Contains(_server.Roles, role => role.Name == "Auditor");
        Assert.Equal(string.Empty, _viewModel.NewRoleName);

        _viewModel.SelectedRole = _viewModel.Roles.Single(role => role.IsBuiltIn);
        Assert.False(_viewModel.DeleteRoleCommand.CanExecute(null));
        Assert.False(_viewModel.AddRuleCommand.CanExecute(null));

        _viewModel.SelectedRole = _viewModel.Roles.Single(role => role.Name == "Operator");
        Assert.True(_viewModel.DeleteRoleCommand.CanExecute(null));
        await _viewModel.DeleteRoleCommand.ExecuteAsync();

        Assert.DoesNotContain(_server.Roles, role => role.Name == "Operator");
        Assert.Null(_viewModel.SelectedRole);
        Assert.Contains("Operator", Assert.Single(_confirmation.Questions));
    }

    [Fact]
    public async Task Rules_CanBeAddedForOneDeviceOrAll_AndRemoved()
    {
        await _viewModel.LoadAsync();
        _viewModel.SelectedRole = _viewModel.Roles.Single(role => role.Name == "Operator");
        _viewModel.SelectedPermission = _viewModel.Permissions.Single(permission => permission.Name == "ManageDevices");
        _viewModel.SelectedDevice = _viewModel.Devices.Single(device => device.Label == "Router");

        await _viewModel.AddRuleCommand.ExecuteAsync();

        var role = _server.Roles.Single(role => role.Name == "Operator");
        Assert.Contains(role.Rules, rule => rule.Permission == "ManageDevices" && rule.DeviceId == "device-1");

        // The role and the choices stay selected, so several rules can be added in a row.
        Assert.Equal("Operator", _viewModel.SelectedRole!.Name);
        Assert.Equal("ManageDevices", _viewModel.SelectedPermission!.Name);

        _viewModel.SelectedDevice = _viewModel.Devices[0];
        await _viewModel.AddRuleCommand.ExecuteAsync();
        Assert.Contains(role.Rules, rule => rule.Permission == "ManageDevices" && rule.DeviceId == string.Empty);

        Assert.False(_viewModel.RemoveRuleCommand.CanExecute(null));
        _viewModel.SelectedRule = _viewModel.SelectedRole!.Rules.Single(rule => rule.Permission == "ViewDevices");
        await _viewModel.RemoveRuleCommand.ExecuteAsync();

        Assert.DoesNotContain(role.Rules, rule => rule.Permission == "ViewDevices");
        Assert.Equal(2, role.Rules.Count);
    }

    [Fact]
    public async Task Load_WhenServerRefuses_SaysWhy()
    {
        _server.Failure = new ServerClientException(ServerErrorKind.AccessDenied, "You do not have permission for this operation.");

        await _viewModel.LoadAsync();

        Assert.Equal("Na túto operáciu nemáte oprávnenie.", _viewModel.Message);
        Assert.Empty(_viewModel.Users);
    }

    [Fact]
    public async Task ChangeOwnPassword_ChecksTheFormFirst_AndReportsAWrongCurrentPassword()
    {
        var viewModel = new ChangePasswordViewModel(_server);

        Assert.False(await viewModel.ChangeAsync(string.Empty, "new-password-1", "new-password-1"));
        Assert.False(await viewModel.ChangeAsync("correct-password", "new-password-1", "different"));
        Assert.Contains("nezhodujú", viewModel.Message);

        Assert.False(await viewModel.ChangeAsync("wrong", "new-password-1", "new-password-1"));
        Assert.Contains("not correct", viewModel.Message);
        Assert.Equal("correct-password", _server.Password);

        Assert.True(await viewModel.ChangeAsync("correct-password", "new-password-1", "new-password-1"));
        Assert.Equal("new-password-1", _server.Password);
    }

    [Fact]
    public void MainWindow_OffersOnlyWhatTheSignedInUserMayDo()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        Assert.False(viewModel.CanAddDevices || viewModel.CanManageSettings || viewModel.CanManageUsers);

        viewModel.ApplySession(new LoginReply { UserName = "jana", Permissions = { "ViewDevices", "ManageSettings" } });

        Assert.Equal("Prihlásený: jana", viewModel.CurrentUserText);
        Assert.True(viewModel.CanManageSettings);
        Assert.False(viewModel.CanAddDevices);
        Assert.False(viewModel.CanManageUsers);

        var ended = 0;
        viewModel.SessionEnded += (_, _) => ended++;
        viewModel.EndSessionAfterPasswordChange();

        Assert.Equal(1, ended);
        Assert.Contains("Heslo bolo zmenené", viewModel.SessionEndMessage);
    }

    private sealed class Confirmation : IConfirmationDialog
    {
        public bool Answer { get; set; } = true;

        public List<string> Questions { get; } = [];

        public bool Confirm(string title, string question)
        {
            Questions.Add(question);

            return Answer;
        }
    }
}
