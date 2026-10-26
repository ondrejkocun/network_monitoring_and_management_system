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
using NetworkMonitoringSystem.Tests.Infrastructure;
using ContractEventSeverity = NetworkMonitoringSystem.Contracts.Admin.EventSeverity;
using DomainEventSeverity = NetworkMonitoringSystem.Domain.Monitoring.EventSeverity;
using Device = NetworkMonitoringSystem.Domain.Devices.Device;
using DomainMonitoringMode = NetworkMonitoringSystem.Domain.Devices.MonitoringMode;
using DomainDeviceStatus = NetworkMonitoringSystem.Domain.Devices.DeviceStatus;
using TrafficTransport = NetworkMonitoringSystem.Domain.Monitoring.TrafficTransport;

/// <summary>
/// Calls the administration API of the real server (hosted in memory) with a real PostgreSQL database,
/// through the same client the desktop application uses.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AdminApiTests : IClassFixture<DatabaseFixture>, IAsyncLifetime, IDisposable
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
            TestAdministrator.Configure(builder);
        });
        _channel = GrpcChannel.ForAddress(
            _server.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _server.Server.CreateHandler() });
        _client = new GrpcServerClient(new AdminApi.AdminApiClient(_channel));
    }

    public async Task InitializeAsync()
    {
        await _client.LoginAsync(TestAdministrator.UserName, TestAdministrator.Password);
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
        Assert.False(device.HasTcpPort);
    }

    [Fact]
    public async Task AddedDevice_WithTcpPort_KeepsIt_AndInvalidPortIsRejected()
    {
        var name = "NAS-" + Guid.NewGuid().ToString("N");

        await _client.AddAgentlessDeviceAsync(name, "192.0.2.80", tcpPort: 445);

        Assert.Equal(445, Assert.Single(await _client.GetDevicesAsync(), device => device.Name == name).TcpPort);

        var exception = await Assert.ThrowsAsync<ServerClientException>(() =>
            _client.AddAgentlessDeviceAsync("NAS-" + Guid.NewGuid().ToString("N"), "192.0.2.81", tcpPort: 70000));
        Assert.Equal(ServerErrorKind.InvalidInput, exception.Kind);
        Assert.Contains("between 1 and 65535", exception.Message);
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
    public async Task TrafficCapture_IsSwitchedOnForAgentDevice_AndItsTrafficIsReadSummedForThePeriod()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var hostName = "pc-" + Guid.NewGuid().ToString("N");
        var device = new Device(Guid.NewGuid(), hostName, DomainMonitoringMode.Agent, hostName, null, now.AddDays(-1));

        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.Devices.Add(device);
            dbContext.TrafficRecords.AddRange(
                new TrafficRecord(device.Id, now.AddMinutes(-3), now.AddMinutes(-2), TrafficTransport.Udp, "DNS", "192.0.2.53", 53, 2, 2, 150, 400),
                new TrafficRecord(device.Id, now.AddMinutes(-2), now.AddMinutes(-1), TrafficTransport.Udp, "DNS", "192.0.2.53", 53, 1, 1, 75, 200),
                new TrafficRecord(device.Id, now.AddMinutes(-2), now.AddMinutes(-1), TrafficTransport.Icmp, "ICMP", "192.0.2.1", null, 4, 4, 296, 296),
                new TrafficRecord(device.Id, now.AddDays(-2), now.AddDays(-2), TrafficTransport.Tcp, "TLSv1.3", "192.0.2.10", 443, 9, 9, 9000, 9000));
            await dbContext.SaveChangesAsync();
        }

        var switchedOn = await _client.SetDeviceTrafficCaptureAsync(device.Id.ToString(), capture: true);
        var entries = await _client.GetDeviceTrafficAsync(device.Id.ToString(), now.AddHours(-1), now);

        Assert.True(switchedOn.CaptureTraffic);
        Assert.Equal(2, entries.Count);
        var dns = entries[0];
        Assert.Equal(("Udp", "DNS", "192.0.2.53", 53), (dns.Transport, dns.Protocol, dns.RemoteAddress, dns.ServicePort));
        Assert.Equal((3L, 225L, 600L), (dns.PacketsSent, dns.BytesSent, dns.BytesReceived));
        Assert.False(entries[1].HasServicePort);

        await using var readContext = _database.CreateDbContext();
        Assert.True(await readContext.Events.AnyAsync(monitoringEvent =>
            monitoringEvent.DeviceId == device.Id && monitoringEvent.Type == MonitoringEventType.TrafficCaptureChanged && monitoringEvent.UserId != null));
    }

    [Fact]
    public async Task TrafficCapture_ForDeviceWithoutAgent_IsRejected()
    {
        var name = "Router-" + Guid.NewGuid().ToString("N");
        await _client.AddAgentlessDeviceAsync(name, "192.0.2.90");
        var router = Assert.Single(await _client.GetDevicesAsync(), device => device.Name == name);

        var exception = await Assert.ThrowsAsync<ServerClientException>(() => _client.SetDeviceTrafficCaptureAsync(router.Id, capture: true));

        Assert.Equal(ServerErrorKind.InvalidInput, exception.Kind);
    }

    [Fact]
    public async Task DisabledDevice_StaysInList_UntilEnabled_AndBothChangesAreLoggedWithTheirAuthor()
    {
        var name = "Printer-" + Guid.NewGuid().ToString("N");
        await _client.AddAgentlessDeviceAsync(name, "192.0.2.70");
        var device = Assert.Single(await _client.GetDevicesAsync(), device => device.Name == name);

        var disabled = await _client.SetDeviceEnabledAsync(device.Id, isEnabled: false);

        Assert.False(disabled.IsEnabled);
        Assert.Equal(DeviceStatus.Unknown, disabled.Status);
        Assert.False(Assert.Single(await _client.GetDevicesAsync(), listed => listed.Id == device.Id).IsEnabled);

        Assert.True((await _client.SetDeviceEnabledAsync(device.Id, isEnabled: true)).IsEnabled);

        await using var dbContext = _database.CreateDbContext();
        var deviceId = Guid.Parse(device.Id);
        var events = await dbContext.Events
            .Where(monitoringEvent => monitoringEvent.DeviceId == deviceId)
            .OrderBy(monitoringEvent => monitoringEvent.Id)
            .ToListAsync();

        Assert.Equal([MonitoringEventType.DeviceDisabled, MonitoringEventType.DeviceEnabled], events.Select(monitoringEvent => monitoringEvent.Type));
        Assert.All(events, monitoringEvent => Assert.NotNull(monitoringEvent.UserId));
    }

    [Theory]
    [InlineData("3f2b0c1e-0000-4000-8000-000000000000")]
    [InlineData("not-an-id")]
    public async Task SetDeviceEnabled_ForDeviceThatDoesNotExist_IsReportedAsNotFound(string id)
    {
        var exception = await Assert.ThrowsAsync<ServerClientException>(() => _client.SetDeviceEnabledAsync(id, isEnabled: false));

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
    public async Task AddDevice_WithDuplicateIpAddress_IsRejected_AndStoresNothing()
    {
        var ipAddress = "192.0.2.60";
        await _client.AddAgentlessDeviceAsync("Router-" + Guid.NewGuid().ToString("N"), ipAddress);
        var before = (await _client.GetDevicesAsync()).Count;

        var exception = await Assert.ThrowsAsync<ServerClientException>(() =>
            _client.AddAgentlessDeviceAsync("Router Copy-" + Guid.NewGuid().ToString("N"), ipAddress));

        Assert.Equal(ServerErrorKind.InvalidInput, exception.Kind);
        Assert.Contains("already exists", exception.Message);
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
}
