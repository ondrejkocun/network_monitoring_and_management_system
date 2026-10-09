namespace NetworkMonitoringSystem.Tests.Server;

using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Contracts.Agents;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Server.Services;
using NetworkMonitoringSystem.Tests.Infrastructure;
using DomainDeviceStatus = NetworkMonitoringSystem.Domain.Devices.DeviceStatus;

/// <summary>
/// Calls the administration API of the real server (hosted in memory) with a real PostgreSQL database,
/// through the same client the desktop application uses.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AdminApiTests : IClassFixture<DatabaseFixture>, IDisposable
{
    private const string Token = "test-enrollment-token";

    private readonly DatabaseFixture _database;
    private readonly WebApplicationFactory<Program> _server;
    private readonly GrpcChannel _channel;
    private readonly GrpcServerClient _client;

    public AdminApiTests(DatabaseFixture database)
    {
        _database = database;
        _server = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // A non-development environment keeps the developer's own settings and secrets out of the test.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Agents:EnrollmentToken", Token);
        });
        _channel = GrpcChannel.ForAddress(
            _server.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _server.Server.CreateHandler() });
        _client = new GrpcServerClient(new AdminApi.AdminApiClient(_channel));
    }

    public void Dispose()
    {
        _channel.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task AddedDevice_AppearsInDeviceList()
    {
        var name = "Router-" + Guid.NewGuid().ToString("N");

        await _client.AddAgentlessDeviceAsync(name, "192.0.2.10");

        var device = Assert.Single(await _client.GetDevicesAsync(), device => device.Name == name);
        Assert.Equal(MonitoringMode.Agentless, device.MonitoringMode);
        Assert.Equal("192.0.2.10", device.IpAddress);
        Assert.Equal(string.Empty, device.HostName);
        Assert.True(device.IsEnabled);
        Assert.True(Guid.TryParse(device.Id, out _));
    }

    [Fact]
    public async Task RemovedDevice_DisappearsFromList_TogetherWithItsHistory()
    {
        var name = "Switch-" + Guid.NewGuid().ToString("N");
        await _client.AddAgentlessDeviceAsync(name, "192.0.2.20");
        var device = Assert.Single(await _client.GetDevicesAsync(), device => device.Name == name);
        var deviceId = Guid.Parse(device.Id);

        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.DeviceSnapshots.Add(new DeviceSnapshot(deviceId, DateTimeOffset.UtcNow, DomainDeviceStatus.Offline));
            dbContext.Outages.Add(new Outage(deviceId, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        await _client.RemoveDeviceAsync(device.Id);

        Assert.DoesNotContain(await _client.GetDevicesAsync(), device => device.Name == name);

        await using (var dbContext = _database.CreateDbContext())
        {
            Assert.False(await dbContext.DeviceSnapshots.AnyAsync(snapshot => snapshot.DeviceId == deviceId));
            Assert.False(await dbContext.Outages.AnyAsync(outage => outage.DeviceId == deviceId));
            Assert.True(await dbContext.Events.AnyAsync(monitoringEvent =>
                monitoringEvent.Type == MonitoringEventType.DeviceRemoved && monitoringEvent.Message.Contains(name)));
        }
    }

    [Theory]
    [InlineData("3f2b0c1e-0000-4000-8000-000000000000")]
    [InlineData("not-an-id")]
    public async Task RemoveDevice_ThatDoesNotExist_IsReportedAsNotFound(string id)
    {
        var exception = await Assert.ThrowsAsync<ServerClientException>(() => _client.RemoveDeviceAsync(id));

        Assert.Equal(ServerErrorKind.NotFound, exception.Kind);
    }

    [Theory]
    [InlineData("Router", "not-an-address")]
    [InlineData("Router", "")]
    [InlineData(" ", "192.0.2.11")]
    public async Task AddDevice_WithInvalidInput_IsRejected_AndStoresNothing(string name, string ipAddress)
    {
        var before = (await _client.GetDevicesAsync()).Count;

        var exception = await Assert.ThrowsAsync<ServerClientException>(() => _client.AddAgentlessDeviceAsync(name, ipAddress));

        Assert.Equal(ServerErrorKind.InvalidInput, exception.Kind);
        Assert.Equal(before, (await _client.GetDevicesAsync()).Count);
    }

    [Fact]
    public async Task ChangedInterval_IsStored_Logged_AndGivenToAgents()
    {
        var updated = await _client.UpdateSyncIntervalAsync(25);

        Assert.Equal(25, updated.SyncIntervalSeconds);
        Assert.Equal(25, (await _client.GetSettingsAsync()).SyncIntervalSeconds);

        await using (var dbContext = _database.CreateDbContext())
        {
            Assert.True(await dbContext.Events.AnyAsync(
                monitoringEvent => monitoringEvent.Type == MonitoringEventType.SettingsChanged));
        }

        // An agent that registers now is told to report in the new interval.
        var registration = await new AgentApi.AgentApiClient(_channel).RegisterAsync(new RegisterRequest
        {
            EnrollmentToken = Token,
            HostName = "pc-" + Guid.NewGuid().ToString("N"),
        });

        Assert.Equal(25, registration.SyncIntervalSeconds);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(86401)]
    public async Task ChangeInterval_OutsideLimits_IsRejected(int seconds)
    {
        var before = (await _client.GetSettingsAsync()).SyncIntervalSeconds;

        var exception = await Assert.ThrowsAsync<ServerClientException>(() => _client.UpdateSyncIntervalAsync(seconds));

        Assert.Equal(ServerErrorKind.InvalidInput, exception.Kind);
        Assert.Equal(before, (await _client.GetSettingsAsync()).SyncIntervalSeconds);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.50.20", false)]
    [InlineData("158.193.96.254", false)]
    public void AdministrationApi_AcceptsOnlyLocalConnections(string? remoteAddress, bool expected)
    {
        var address = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);

        Assert.Equal(expected, LocalOnlyInterceptor.IsLocal(address));
    }
}
