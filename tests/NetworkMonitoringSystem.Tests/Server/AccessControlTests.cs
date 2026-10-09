namespace NetworkMonitoringSystem.Tests.Server;

using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Infrastructure;
using Device = NetworkMonitoringSystem.Domain.Devices.Device;
using DomainMonitoringMode = NetworkMonitoringSystem.Domain.Devices.MonitoringMode;

/// <summary>
/// Verifies on the real server with a real database that the administration API serves only signed-in users
/// and only what their roles allow.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AccessControlTests : IClassFixture<DatabaseFixture>, IDisposable
{
    private const string Password = "operator-password";

    private readonly DatabaseFixture _database;
    private readonly WebApplicationFactory<Program> _server;
    private readonly GrpcChannel _channel;

    public AccessControlTests(DatabaseFixture database)
    {
        _database = database;
        _server = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Agents:EnrollmentToken", "test-enrollment-token");
            TestAdministrator.Configure(builder);
        });
        _channel = GrpcChannel.ForAddress(
            _server.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _server.Server.CreateHandler() });
    }

    public void Dispose()
    {
        _channel.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task CallWithoutSession_OrWithAnInventedToken_IsRefused()
    {
        var withoutSession = await Assert.ThrowsAsync<ServerClientException>(() => NewClient().GetDevicesAsync());
        Assert.Equal(ServerErrorKind.NotSignedIn, withoutSession.Kind);

        var invented = new AdminApi.AdminApiClient(TestAdministrator.WithToken(_channel, new string('A', 64)));
        var exception = await Assert.ThrowsAsync<RpcException>(async () => await invented.ListDevicesAsync(new ListDevicesRequest()));
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsRefused_AndLogged()
    {
        var client = NewClient();

        var exception = await Assert.ThrowsAsync<ServerClientException>(
            () => client.LoginAsync(TestAdministrator.UserName, "not-the-password"));

        Assert.Equal(ServerErrorKind.NotSignedIn, exception.Kind);

        await using var dbContext = _database.CreateDbContext();
        Assert.True(await dbContext.Events.AnyAsync(monitoringEvent => monitoringEvent.Type == MonitoringEventType.LoginFailed));
    }

    [Fact]
    public async Task Administrator_HoldsAllPermissions_AndTheStoredPasswordIsAHash()
    {
        var client = NewClient();

        var session = await client.LoginAsync(TestAdministrator.UserName, TestAdministrator.Password);

        Assert.Equal(Enum.GetNames<Permission>().Order(), session.Permissions.Order());

        await using var dbContext = _database.CreateDbContext();
        var administrator = await dbContext.Users.SingleAsync(user => user.UserName == TestAdministrator.UserName);
        Assert.StartsWith("pbkdf2-sha256$", administrator.PasswordHash);
        Assert.DoesNotContain(TestAdministrator.Password, administrator.PasswordHash);

        // The session token is not stored either, only its hash.
        Assert.False(await dbContext.UserSessions.AnyAsync(stored => stored.TokenHash == session.Token));
    }

    [Fact]
    public async Task UserWithoutPermission_IsRefusedByTheServer_AndTheRefusalIsLoggedOnce()
    {
        var device = await AddDeviceAsync();
        var user = await AddUserAsync(role => role.Allow(Permission.ViewDevices));
        var client = NewClient();
        await client.LoginAsync(user.UserName, Password);

        // Viewing is allowed.
        Assert.Contains(await client.GetDevicesAsync(), listed => listed.Id == device.Id.ToString());
        Assert.Equal(86400, (await client.GetSettingsAsync()).MaxSyncIntervalSeconds);

        // Changing anything is not.
        var remove = await Assert.ThrowsAsync<ServerClientException>(() => client.RemoveDeviceAsync(device.Id.ToString()));
        var add = await Assert.ThrowsAsync<ServerClientException>(() => client.AddAgentlessDeviceAsync("Router", "192.0.2.50"));
        var interval = await Assert.ThrowsAsync<ServerClientException>(() => client.UpdateSyncIntervalAsync(30));
        Assert.All([remove, add, interval], exception => Assert.Equal(ServerErrorKind.AccessDenied, exception.Kind));

        // Repeating the refused call does not add another event.
        await Assert.ThrowsAsync<ServerClientException>(() => client.RemoveDeviceAsync(device.Id.ToString()));

        await using var dbContext = _database.CreateDbContext();
        Assert.True(await dbContext.Devices.AnyAsync(stored => stored.Id == device.Id));

        var denied = await dbContext.Events
            .Where(monitoringEvent => monitoringEvent.Type == MonitoringEventType.AccessDenied && monitoringEvent.UserId == user.Id)
            .ToListAsync();
        Assert.Equal(3, denied.Count);

        var removal = Assert.Single(denied, monitoringEvent => monitoringEvent.Message.Contains("RemoveDevice"));
        Assert.Equal(device.Id, removal.DeviceId);
        Assert.Contains(user.UserName, removal.Message);
    }

    [Fact]
    public async Task UserWithRuleForOneDevice_SeesAndManagesOnlyThatDevice()
    {
        var allowed = await AddDeviceAsync();
        var other = await AddDeviceAsync();
        var user = await AddUserAsync(role =>
        {
            role.Allow(Permission.ViewDevices, allowed.Id);
            role.Allow(Permission.ManageDevices, allowed.Id);
        });
        var client = NewClient();
        var session = await client.LoginAsync(user.UserName, Password);

        // Permissions for a single device are not reported as general ones.
        Assert.Empty(session.Permissions);

        var listed = Assert.Single(await client.GetDevicesAsync());
        Assert.Equal(allowed.Id.ToString(), listed.Id);

        var now = DateTimeOffset.UtcNow;
        await client.GetDeviceResourcesAsync(allowed.Id.ToString());
        await client.GetDeviceActivityAsync(allowed.Id.ToString());
        await client.GetDeviceHistoryAsync(allowed.Id.ToString(), now.AddHours(-1), now);

        var refused = new Func<Task>[]
        {
            () => client.GetDeviceResourcesAsync(other.Id.ToString()),
            () => client.GetDeviceActivityAsync(other.Id.ToString()),
            () => client.GetDeviceHistoryAsync(other.Id.ToString(), now.AddHours(-1), now),
            () => client.RemoveDeviceAsync(other.Id.ToString()),
            // A device that does not exist is refused the same way, so the answer does not reveal which devices exist.
            () => client.GetDeviceResourcesAsync(Guid.NewGuid().ToString()),
            // Adding a device concerns no single device, so a rule for one is not enough.
            () => client.AddAgentlessDeviceAsync("Router", "192.0.2.51"),
        };

        foreach (var call in refused)
        {
            Assert.Equal(ServerErrorKind.AccessDenied, (await Assert.ThrowsAsync<ServerClientException>(call)).Kind);
        }

        await client.RemoveDeviceAsync(allowed.Id.ToString());

        await using var dbContext = _database.CreateDbContext();
        Assert.False(await dbContext.Devices.AnyAsync(stored => stored.Id == allowed.Id));
        Assert.True(await dbContext.Devices.AnyAsync(stored => stored.Id == other.Id));
    }

    [Fact]
    public async Task UserWithoutAnyRole_SeesNothing()
    {
        var user = await AddUserAsync(_ => { });
        var client = NewClient();
        await client.LoginAsync(user.UserName, Password);

        var exception = await Assert.ThrowsAsync<ServerClientException>(() => client.GetDevicesAsync());

        Assert.Equal(ServerErrorKind.AccessDenied, exception.Kind);
    }

    [Fact]
    public async Task SignedOutSession_AndSessionOfDeactivatedUser_StopWorking()
    {
        var user = await AddUserAsync(role => role.Allow(Permission.ViewDevices));
        var signedOut = NewClient();
        var deactivated = NewClient();
        var endedSession = await signedOut.LoginAsync(user.UserName, Password);
        var session = await deactivated.LoginAsync(user.UserName, Password);

        await signedOut.LogoutAsync();

        // The token itself is refused, not only forgotten by the client.
        var afterLogout = new AdminApi.AdminApiClient(TestAdministrator.WithToken(_channel, endedSession.Token));
        var refused = await Assert.ThrowsAsync<RpcException>(async () => await afterLogout.ListDevicesAsync(new ListDevicesRequest()));
        Assert.Equal(StatusCode.Unauthenticated, refused.StatusCode);
        await deactivated.GetDevicesAsync();

        await using (var dbContext = _database.CreateDbContext())
        {
            (await dbContext.Users.SingleAsync(stored => stored.Id == user.Id)).Deactivate();
            await dbContext.SaveChangesAsync();
        }

        var raw = new AdminApi.AdminApiClient(TestAdministrator.WithToken(_channel, session.Token));
        var exception = await Assert.ThrowsAsync<RpcException>(async () => await raw.ListDevicesAsync(new ListDevicesRequest()));
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    private GrpcServerClient NewClient() => new(new AdminApi.AdminApiClient(_channel));

    private async Task<Device> AddDeviceAsync()
    {
        var device = new Device(
            Guid.NewGuid(),
            "Device-" + Guid.NewGuid().ToString("N"),
            DomainMonitoringMode.Agentless,
            null,
            IPAddress.Parse("192.0.2.40"),
            DateTimeOffset.UtcNow);

        await using var dbContext = _database.CreateDbContext();
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync();

        return device;
    }

    /// <summary>Stores a user with one role of their own, configured by the caller.</summary>
    private async Task<User> AddUserAsync(Action<Role> configureRole)
    {
        var role = new Role(Guid.NewGuid(), "Role-" + Guid.NewGuid().ToString("N"));
        configureRole(role);

        var user = new User(Guid.NewGuid(), "user-" + Guid.NewGuid().ToString("N"), PasswordHasher.Hash(Password), DateTimeOffset.UtcNow);
        user.AssignRole(role);

        await using var dbContext = _database.CreateDbContext();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }
}
