namespace NetworkMonitoringSystem.Tests.Application;

using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class AccessManagementServiceTests
{
    private const string Password = "correct horse battery";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly MutableTimeProvider _clock = new(Now);
    private readonly AccessManagementService _service;
    private readonly AuthenticationService _authentication;
    private readonly Role _administratorRole;
    private readonly User _administrator;
    private readonly AuthenticatedUser _actor;

    public AccessManagementServiceTests()
    {
        var unitOfWork = new CountingUnitOfWork();

        _service = new AccessManagementService(_users, _devices, _history, unitOfWork, _clock);
        _authentication = new AuthenticationService(_users, _history, unitOfWork, Options.Create(new AccessOptions()), _clock);

        _administratorRole = new Role(Guid.NewGuid(), AuthenticationService.AdministratorRoleName);
        _administratorRole.Allow(Permission.ManageUsers);
        _users.AddRole(_administratorRole);

        _administrator = new User(Guid.NewGuid(), "admin", PasswordHasher.Hash(Password), Now);
        _administrator.AssignRole(_administratorRole);
        _users.AddUser(_administrator);

        _actor = AuthenticatedUser.FromUser(_administrator);
    }

    [Fact]
    public async Task CreatedUser_CanSignIn_WithTheirRoles_AndTheChangeIsLoggedWithItsAuthor()
    {
        var role = await _service.CreateRoleAsync(_actor, "Operator");
        await _service.AllowAsync(_actor, role.Id, Permission.ViewDevices, deviceId: null);

        var created = await _service.CreateUserAsync(_actor, " Jana ", Password, [role.Id]);

        Assert.Equal("jana", created.UserName);
        Assert.True(created.IsActive);
        Assert.Equal([role.Id], created.RoleIds);

        var signedIn = (await _authentication.LoginAsync("jana", Password)).User;
        Assert.True(signedIn.Can(Permission.ViewDevices));
        Assert.False(signedIn.Can(Permission.ManageUsers));

        var logged = _history.Events.Last(monitoringEvent => monitoringEvent.Type == MonitoringEventType.AccessChanged);
        Assert.Equal(_administrator.Id, logged.UserId);
        Assert.Contains("created user 'jana'", logged.Message);
        Assert.DoesNotContain(Password, logged.Message);
    }

    [Theory]
    [InlineData("admin", Password)]
    [InlineData(" ADMIN ", Password)]
    [InlineData("", Password)]
    [InlineData("jana", "short")]
    public async Task CreateUser_WithTakenOrEmptyName_OrShortPassword_IsRefused(string userName, string password)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateUserAsync(_actor, userName, password, []));

        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task CreateUser_WithUnknownRole_IsRefused()
    {
        await Assert.ThrowsAsync<AccessItemNotFoundException>(() => _service.CreateUserAsync(_actor, "jana", Password, [Guid.NewGuid()]));

        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task DeactivatedUser_IsSignedOutEverywhere_AndCanBeActivatedAgain()
    {
        var user = await _service.CreateUserAsync(_actor, "jana", Password, []);
        var session = await _authentication.LoginAsync("jana", Password);

        Assert.False((await _service.SetUserActiveAsync(_actor, user.Id, isActive: false)).IsActive);

        Assert.Empty(_users.Sessions);
        Assert.Null(await _authentication.AuthenticateAsync(session.Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _authentication.LoginAsync("jana", Password));

        Assert.True((await _service.SetUserActiveAsync(_actor, user.Id, isActive: true)).IsActive);
        Assert.NotNull(await _authentication.LoginAsync("jana", Password));
    }

    [Fact]
    public async Task LastActiveAdministrator_CannotBeDeactivated_NorLoseTheRole_UntilThereIsAnother()
    {
        await Assert.ThrowsAsync<AccessChangeRefusedException>(() => _service.SetUserActiveAsync(_actor, _administrator.Id, isActive: false));
        await Assert.ThrowsAsync<AccessChangeRefusedException>(() => _service.SetUserRolesAsync(_actor, _administrator.Id, []));
        Assert.True(_administrator.IsActive);
        Assert.Single(_administrator.Roles);

        // A deactivated administrator does not count as another one.
        var second = await _service.CreateUserAsync(_actor, "second", Password, [_administratorRole.Id]);
        await _service.SetUserActiveAsync(_actor, second.Id, isActive: false);
        await Assert.ThrowsAsync<AccessChangeRefusedException>(() => _service.SetUserActiveAsync(_actor, _administrator.Id, isActive: false));

        await _service.SetUserActiveAsync(_actor, second.Id, isActive: true);
        await _service.SetUserRolesAsync(_actor, _administrator.Id, []);

        Assert.Empty(_administrator.Roles);
    }

    [Fact]
    public async Task SetUserRoles_ReplacesTheRolesTheUserHad()
    {
        var first = await _service.CreateRoleAsync(_actor, "First");
        var second = await _service.CreateRoleAsync(_actor, "Second");
        var user = await _service.CreateUserAsync(_actor, "jana", Password, [first.Id]);

        var updated = await _service.SetUserRolesAsync(_actor, user.Id, [second.Id, second.Id]);

        Assert.Equal([second.Id], updated.RoleIds);
    }

    [Fact]
    public async Task ResetPassword_ReplacesThePassword_UnlocksTheAccount_AndSignsTheUserOut()
    {
        var user = await _service.CreateUserAsync(_actor, "jana", Password, []);
        var session = await _authentication.LoginAsync("jana", Password);

        for (var attempt = 0; attempt < User.MaxFailedLogins; attempt++)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => _authentication.LoginAsync("jana", "wrong password"));
        }

        await _service.ResetPasswordAsync(_actor, user.Id, "a brand new password");

        Assert.Null(await _authentication.AuthenticateAsync(session.Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _authentication.LoginAsync("jana", Password));
        Assert.NotNull(await _authentication.LoginAsync("jana", "a brand new password"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ResetPasswordAsync(_actor, user.Id, "short"));
        await Assert.ThrowsAsync<AccessItemNotFoundException>(() => _service.ResetPasswordAsync(_actor, Guid.NewGuid(), Password));
    }

    [Fact]
    public async Task ChangeOwnPassword_NeedsTheCurrentOne_AndSignsTheUserOutEverywhere()
    {
        var session = await _authentication.LoginAsync("admin", Password);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _service.ChangeOwnPasswordAsync(_actor, "wrong password", "a brand new password"));
        Assert.NotNull(await _authentication.AuthenticateAsync(session.Token));
        Assert.Equal(1, _administrator.FailedLoginCount);

        await _service.ChangeOwnPasswordAsync(_actor, Password, "a brand new password");

        Assert.Null(await _authentication.AuthenticateAsync(session.Token));
        Assert.NotNull(await _authentication.LoginAsync("admin", "a brand new password"));
    }

    [Fact]
    public async Task ChangeOwnPassword_DoesNotAllowGuessingTheCurrentOneWithoutLimit()
    {
        for (var attempt = 0; attempt < User.MaxFailedLogins; attempt++)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => _service.ChangeOwnPasswordAsync(_actor, "wrong password", "a brand new password"));
        }

        // The account is locked now, so even the right current password is refused.
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _service.ChangeOwnPasswordAsync(_actor, Password, "a brand new password"));
        Assert.True(PasswordHasher.Verify(Password, _administrator.PasswordHash));
    }

    [Fact]
    public async Task Rules_AreAddedOnce_ForAllDevicesOrOneExistingDevice_AndCanBeRevoked()
    {
        var device = new Device(Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, System.Net.IPAddress.Loopback, Now);
        _devices.Add(device);
        var role = await _service.CreateRoleAsync(_actor, "Operator");

        await _service.AllowAsync(_actor, role.Id, Permission.ViewDevices, deviceId: null);
        await _service.AllowAsync(_actor, role.Id, Permission.ViewDevices, deviceId: null);
        var withDeviceRule = await _service.AllowAsync(_actor, role.Id, Permission.ManageDevices, device.Id);

        Assert.Equal(
            [new GrantedPermission(Permission.ViewDevices, null), new GrantedPermission(Permission.ManageDevices, device.Id)],
            withDeviceRule.Rules);
        Assert.Contains("device 'Router'", _history.Events[^1].Message);

        await Assert.ThrowsAsync<AccessItemNotFoundException>(() => _service.AllowAsync(_actor, role.Id, Permission.ViewDevices, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AllowAsync(_actor, role.Id, (Permission)99, deviceId: null));

        var afterRevoke = await _service.RevokeAsync(_actor, role.Id, Permission.ViewDevices, deviceId: null);
        Assert.Equal([new GrantedPermission(Permission.ManageDevices, device.Id)], afterRevoke.Rules);
    }

    [Fact]
    public async Task BuiltInRole_CannotBeChangedOrDeleted_AndRoleNamesAreUnique()
    {
        await Assert.ThrowsAsync<AccessChangeRefusedException>(() => _service.DeleteRoleAsync(_actor, _administratorRole.Id));
        await Assert.ThrowsAsync<AccessChangeRefusedException>(() => _service.RevokeAsync(_actor, _administratorRole.Id, Permission.ManageUsers, null));
        await Assert.ThrowsAsync<AccessChangeRefusedException>(() => _service.AllowAsync(_actor, _administratorRole.Id, Permission.ViewDevices, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateRoleAsync(_actor, "administrator"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateRoleAsync(_actor, " "));

        Assert.True((await _service.GetRolesAsync()).Single().IsBuiltIn);
    }

    [Fact]
    public async Task DeletedRole_IsTakenFromItsUsers()
    {
        var role = await _service.CreateRoleAsync(_actor, "Operator");
        var user = await _service.CreateUserAsync(_actor, "jana", Password, [role.Id]);

        await _service.DeleteRoleAsync(_actor, role.Id);

        Assert.Empty((await _service.GetUsersAsync()).Single(listed => listed.Id == user.Id).RoleIds);
        await Assert.ThrowsAsync<AccessItemNotFoundException>(() => _service.DeleteRoleAsync(_actor, role.Id));
    }

    [Fact]
    public async Task ExpiredSessions_AreDeleted_AndValidOnesKept()
    {
        await _authentication.LoginAsync("admin", Password);
        _clock.Advance(TimeSpan.FromHours(11));
        await _authentication.LoginAsync("admin", Password);
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(1, await _service.DeleteExpiredSessionsAsync());

        Assert.Single(_users.Sessions);
    }
}
