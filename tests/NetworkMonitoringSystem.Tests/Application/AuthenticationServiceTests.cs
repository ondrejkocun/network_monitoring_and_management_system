namespace NetworkMonitoringSystem.Tests.Application;

using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class AuthenticationServiceTests
{
    private const string Password = "correct horse battery";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly MutableTimeProvider _clock = new(Now);
    private readonly AccessOptions _options = new();

    private AuthenticationService Service => new(_users, _history, new CountingUnitOfWork(), Options.Create(_options), _clock);

    [Fact]
    public void PasswordHash_VerifiesOnlyTheRightPassword_AndDiffersForTheSamePassword()
    {
        var first = PasswordHasher.Hash(Password);
        var second = PasswordHasher.Hash(Password);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(Password, first);
        Assert.True(PasswordHasher.Verify(Password, first));
        Assert.True(PasswordHasher.Verify(Password, second));
        Assert.False(PasswordHasher.Verify(Password + "x", first));
        Assert.False(PasswordHasher.Verify(string.Empty, first));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$***$AAAA")]
    [InlineData("md5$1$AAAA$AAAA")]
    public void PasswordHash_ThatCannotBeRead_VerifiesNothing(string storedHash)
    {
        Assert.False(PasswordHasher.Verify(Password, storedHash));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("1234567")]
    public void PasswordHash_RefusesShortPasswords(string password)
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(password));
    }

    [Fact]
    public async Task Login_WithRightPassword_StartsSession_ThatAuthenticatesTheUserWithTheirPermissions()
    {
        var deviceId = Guid.NewGuid();
        var role = new Role(Guid.NewGuid(), "Operator");
        role.Allow(Permission.ViewDevices);
        role.Allow(Permission.ManageDevices, deviceId);
        AddUser("Jana", role);

        // The name is not case sensitive and may carry spaces around it.
        var result = await Service.LoginAsync("  JANA ", Password);

        Assert.Equal("jana", result.User.UserName);
        Assert.Equal(Now.AddHours(12), result.ExpiresAt);
        Assert.Equal(64, result.Token.Length);

        // Only a hash of the token is stored.
        var session = Assert.Single(_users.Sessions);
        Assert.NotEqual(result.Token, session.TokenHash);

        var user = await Service.AuthenticateAsync(result.Token);
        Assert.NotNull(user);
        Assert.True(user.Can(Permission.ViewDevices));
        Assert.True(user.CanOnDevice(Permission.ViewDevices, Guid.NewGuid()));
        Assert.False(user.Can(Permission.ManageDevices));
        Assert.True(user.CanOnDevice(Permission.ManageDevices, deviceId));
        Assert.False(user.CanOnDevice(Permission.ManageDevices, Guid.NewGuid()));
        Assert.True(user.CanOnSomeDevice(Permission.ManageDevices));
        Assert.False(user.CanOnSomeDevice(Permission.ManageUsers));

        Assert.Equal(MonitoringEventType.UserLoggedIn, Assert.Single(_history.Events).Type);
    }

    [Fact]
    public async Task Login_WithWrongPassword_OrUnknownUser_FailsTheSameWay_AndIsLogged()
    {
        var user = AddUser("jana");

        var wrongPassword = await Assert.ThrowsAsync<AuthenticationFailedException>(() => Service.LoginAsync("jana", "wrong password"));
        var unknownUser = await Assert.ThrowsAsync<AuthenticationFailedException>(() => Service.LoginAsync("nobody", Password));

        Assert.Equal(wrongPassword.Message, unknownUser.Message);
        Assert.Empty(_users.Sessions);
        Assert.Equal(1, user.FailedLoginCount);

        Assert.Equal(2, _history.Events.Count);
        Assert.All(_history.Events, monitoringEvent => Assert.Equal(MonitoringEventType.LoginFailed, monitoringEvent.Type));
        Assert.Equal(user.Id, _history.Events[0].UserId);

        // What was typed as the name of an unknown user is not written down; it may be a password.
        Assert.DoesNotContain("nobody", _history.Events[1].Message);
    }

    [Fact]
    public async Task Account_IsLockedAfterRepeatedWrongPasswords_AndRefusesEvenTheRightOne_UntilTheLockEnds()
    {
        var user = AddUser("jana");

        for (var attempt = 0; attempt < User.MaxFailedLogins; attempt++)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => Service.LoginAsync("jana", "wrong password"));
        }

        Assert.True(user.IsLocked(_clock.GetUtcNow()));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Service.LoginAsync("jana", Password));
        Assert.Contains("locked", _history.Events[^1].Message);

        _clock.Advance(User.LockDuration + TimeSpan.FromSeconds(1));

        Assert.NotNull(await Service.LoginAsync("jana", Password));
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsTheCountOfWrongPasswords()
    {
        var user = AddUser("jana");
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Service.LoginAsync("jana", "wrong password"));

        await Service.LoginAsync("jana", Password);

        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public async Task DeactivatedUser_CannotSignIn_AndTheirOpenSessionStopsWorking()
    {
        var user = AddUser("jana");
        var session = await Service.LoginAsync("jana", Password);

        user.Deactivate();

        Assert.Null(await Service.AuthenticateAsync(session.Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Service.LoginAsync("jana", Password));
    }

    [Fact]
    public async Task Session_StopsWorking_WhenItExpires_OrTheUserSignsOut()
    {
        AddUser("jana");
        var expiring = await Service.LoginAsync("jana", Password);
        var signedOut = await Service.LoginAsync("jana", Password);

        await Service.LogoutAsync(signedOut.Token);
        Assert.Null(await Service.AuthenticateAsync(signedOut.Token));
        Assert.NotNull(await Service.AuthenticateAsync(expiring.Token));

        _clock.Advance(TimeSpan.FromHours(12));
        Assert.Null(await Service.AuthenticateAsync(expiring.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task UnknownToken_AuthenticatesNobody(string token)
    {
        AddUser("jana");
        await Service.LoginAsync("jana", Password);

        Assert.Null(await Service.AuthenticateAsync(token));
    }

    [Fact]
    public async Task RoleChange_TakesEffectForAnOpenSession()
    {
        var role = new Role(Guid.NewGuid(), "Operator");
        role.Allow(Permission.ViewDevices);
        AddUser("jana", role);
        var session = await Service.LoginAsync("jana", Password);

        role.Revoke(Permission.ViewDevices);

        Assert.False((await Service.AuthenticateAsync(session.Token))!.Can(Permission.ViewDevices));
    }

    [Fact]
    public async Task InitialAdministrator_IsCreatedOnce_WithAllPermissions_WhenPasswordIsConfigured()
    {
        _options.InitialAdminPassword = Password;

        Assert.True(await Service.EnsureInitialAdministratorAsync());
        Assert.False(await Service.EnsureInitialAdministratorAsync());

        var administrator = Assert.Single(_users.Users);
        Assert.Equal("admin", administrator.UserName);
        Assert.NotEqual(Password, administrator.PasswordHash);
        Assert.Single(_users.Roles);

        var signedIn = (await Service.LoginAsync("admin", Password)).User;
        Assert.All(Enum.GetValues<Permission>(), permission => Assert.True(signedIn.Can(permission)));
    }

    [Fact]
    public async Task InitialAdministrator_IsNotCreated_WithoutConfiguredPassword_OrWhenUsersExist()
    {
        Assert.False(await Service.EnsureInitialAdministratorAsync());
        Assert.Empty(_users.Users);

        // The administrator role is prepared regardless.
        Assert.Equal(AuthenticationService.AdministratorRoleName, Assert.Single(_users.Roles).Name);

        AddUser("jana");
        _options.InitialAdminPassword = Password;

        Assert.False(await Service.EnsureInitialAdministratorAsync());
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task DeniedAccess_IsWrittenToTheEventLog_WithTheUserAndTheDevice()
    {
        var user = new AuthenticatedUser(Guid.NewGuid(), "jana", []);
        var deviceId = Guid.NewGuid();

        await Service.RecordAccessDeniedAsync(user, "RemoveDevice", deviceId);

        var denied = Assert.Single(_history.Events);
        Assert.Equal((MonitoringEventType.AccessDenied, EventSeverity.Warning), (denied.Type, denied.Severity));
        Assert.Equal((user.Id, deviceId), (denied.UserId, denied.DeviceId));
        Assert.Contains("jana", denied.Message);
        Assert.Contains("RemoveDevice", denied.Message);
    }

    [Fact]
    public void Role_DoesNotHoldTheSameRuleTwice_AndAUserTheSameRoleTwice()
    {
        var role = new Role(Guid.NewGuid(), " Operator ");
        var deviceId = Guid.NewGuid();

        Assert.Equal("Operator", role.Name);
        Assert.True(role.Allow(Permission.ViewDevices));
        Assert.False(role.Allow(Permission.ViewDevices));
        Assert.True(role.Allow(Permission.ViewDevices, deviceId));
        Assert.Equal(2, role.Rules.Count);
        Assert.True(role.Revoke(Permission.ViewDevices, deviceId));
        Assert.False(role.Revoke(Permission.ViewDevices, deviceId));

        var user = new User(Guid.NewGuid(), "jana", "hash", Now);
        Assert.True(user.AssignRole(role));
        Assert.False(user.AssignRole(role));
        Assert.True(user.RemoveRole(role.Id));
        Assert.Empty(user.Roles);
    }

    private User AddUser(string userName, params Role[] roles)
    {
        var user = new User(Guid.NewGuid(), userName, PasswordHasher.Hash(Password), Now);

        foreach (var role in roles)
        {
            user.AssignRole(role);
        }

        _users.AddUser(user);

        return user;
    }
}
