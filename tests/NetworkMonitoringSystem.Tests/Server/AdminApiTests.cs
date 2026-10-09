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
using ContractEventSeverity = NetworkMonitoringSystem.Contracts.Admin.EventSeverity;
using DomainEventSeverity = NetworkMonitoringSystem.Domain.Monitoring.EventSeverity;
using Device = NetworkMonitoringSystem.Domain.Devices.Device;
using DomainMonitoringMode = NetworkMonitoringSystem.Domain.Devices.MonitoringMode;
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

    [Fact]
    public async Task DeviceHistory_ReturnsAvailabilityOutagesEventsAndResources_OfThePeriod()
    {
        // Whole seconds, so the database's precision does not change the lengths.
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var outageStart = now.AddSeconds(-20);
        var outageEnd = now.AddSeconds(-10);
        var deviceId = Guid.NewGuid();

        await using (var dbContext = _database.CreateDbContext())
        {
            // Stored directly, because a device added through the API would only exist from this moment.
            dbContext.Devices.Add(new Device(deviceId, "Server", DomainMonitoringMode.Agentless, null, IPAddress.Parse("192.0.2.30"), now.AddDays(-7)));

            var outage = new Outage(deviceId, outageStart);
            outage.End(outageEnd);
            dbContext.Outages.Add(outage);
            // Ended before the period asked for.
            var earlier = new Outage(deviceId, now.AddDays(-3));
            earlier.End(now.AddDays(-2));
            dbContext.Outages.Add(earlier);

            dbContext.Events.Add(new MonitoringEvent(
                outageStart, MonitoringEventType.DeviceWentOffline, DomainEventSeverity.Warning, "went offline", deviceId));
            dbContext.Events.Add(new MonitoringEvent(
                outageEnd, MonitoringEventType.DeviceWentOnline, DomainEventSeverity.Info, "went online", deviceId));
            dbContext.Events.Add(new MonitoringEvent(
                now.AddDays(-2), MonitoringEventType.PortOpened, DomainEventSeverity.Info, "before the period", deviceId));

            var measured = new DeviceSnapshot(deviceId, now.AddSeconds(-5), DomainDeviceStatus.Online);
            measured.SetResources(new ResourceUsage(42.5, 1000, 250, [], []));
            dbContext.DeviceSnapshots.Add(measured);
            dbContext.DeviceSnapshots.Add(new DeviceSnapshot(deviceId, outageStart, DomainDeviceStatus.Offline));

            await dbContext.SaveChangesAsync();
        }

        var history = await _client.GetDeviceHistoryAsync(deviceId.ToString(), now.AddHours(-1), now);

        var returnedOutage = Assert.Single(history.Outages);
        Assert.Equal(10, (returnedOutage.EndedAt.ToDateTimeOffset() - returnedOutage.StartedAt.ToDateTimeOffset()).TotalSeconds, precision: 3);
        Assert.Equal(10, history.DowntimeSeconds);
        Assert.True(history.HasAvailabilityPercent);
        Assert.Equal(100.0 * 3590 / 3600, history.AvailabilityPercent, precision: 6);

        Assert.Equal(["went online", "went offline"], history.Events.Select(monitoringEvent => monitoringEvent.Message));
        Assert.Equal("DeviceWentOnline", history.Events[0].Type);
        Assert.Equal(ContractEventSeverity.Warning, history.Events[1].Severity);
        Assert.False(history.EventsTruncated);

        var point = Assert.Single(history.ResourcePoints);
        Assert.Equal((42.5, 250UL, 1000UL), (point.CpuUsagePercent, point.MemoryUsedBytes, point.MemoryTotalBytes));
    }

    [Fact]
    public async Task DeviceHistory_OfUnknownDevice_IsNotFound_AndInvalidPeriodIsRejected()
    {
        var now = DateTimeOffset.UtcNow;

        var notFound = await Assert.ThrowsAsync<ServerClientException>(
            () => _client.GetDeviceHistoryAsync(Guid.NewGuid().ToString(), now.AddHours(-1), now));
        Assert.Equal(ServerErrorKind.NotFound, notFound.Kind);

        var invalid = await Assert.ThrowsAsync<ServerClientException>(
            () => _client.GetDeviceHistoryAsync(Guid.NewGuid().ToString(), now, now.AddHours(-1)));
        Assert.Equal(ServerErrorKind.InvalidInput, invalid.Kind);
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
