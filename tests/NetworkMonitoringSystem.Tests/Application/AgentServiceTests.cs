namespace NetworkMonitoringSystem.Tests.Application;

using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class AgentServiceTests
{
    private const string Token = "enrollment-token";

    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly InMemoryDeviceActivityRepository _activity;
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly MonitoringSettings _settings = new(syncIntervalSeconds: 120);
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    public AgentServiceTests()
    {
        _activity = new InMemoryDeviceActivityRepository(_history);
    }

    [Fact]
    public async Task RegisterAsync_CreatesOnlineAgentDevice_AndReturnsCredentialsAndInterval()
    {
        var registration = await CreateService().RegisterAsync(new RegisterAgentRequest(Token, " PC-01 ", "Windows 11"));

        var device = await _devices.GetByIdAsync(registration.Credentials.DeviceId);

        Assert.NotNull(device);
        Assert.Equal("PC-01", device.HostName);
        Assert.Equal(MonitoringMode.Agent, device.MonitoringMode);
        Assert.Equal("Windows 11", device.OperatingSystem);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(_time.GetUtcNow(), device.LastSeenAt);
        Assert.Equal(120, registration.SyncIntervalSeconds);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RegisterAsync_WritesRegistrationAndOnlineEvents_AndSnapshot()
    {
        var registration = await CreateService().RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));

        Assert.Equal(
            [MonitoringEventType.AgentRegistered, MonitoringEventType.DeviceWentOnline],
            _history.Events.Select(monitoringEvent => monitoringEvent.Type));
        Assert.All(_history.Events, monitoringEvent => Assert.Equal(registration.Credentials.DeviceId, monitoringEvent.DeviceId));

        var snapshot = Assert.Single(_history.Snapshots);
        Assert.Equal(DeviceStatus.Online, snapshot.Status);
        Assert.Equal(_time.GetUtcNow(), snapshot.RecordedAt);
    }

    [Fact]
    public async Task RegisterAsync_StoresOnlyHashOfTheKey()
    {
        var registration = await CreateService().RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));

        var device = await _devices.GetByIdAsync(registration.Credentials.DeviceId);

        Assert.NotEmpty(registration.Credentials.AgentKey);
        Assert.NotNull(device!.AgentKeyHash);
        Assert.NotEqual(registration.Credentials.AgentKey, device.AgentKeyHash);
    }

    [Theory]
    [InlineData("wrong-token")]
    [InlineData("")]
    public async Task RegisterAsync_Throws_WhenTokenIsWrong(string token)
    {
        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => CreateService().RegisterAsync(new RegisterAgentRequest(token, "PC-01", null)));

        Assert.Empty(await _devices.GetAllAsync());
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RegisterAsync_Throws_WhenServerHasNoTokenConfigured(string? configuredToken)
    {
        var service = CreateService(configuredToken);

        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => service.RegisterAsync(new RegisterAgentRequest(configuredToken ?? string.Empty, "PC-01", null)));
    }

    [Fact]
    public async Task RegisterAsync_Throws_WhenDeviceWithSameHostNameIsOnline()
    {
        var service = CreateService();
        var first = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));

        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => service.RegisterAsync(new RegisterAgentRequest(Token, "pc-01", null)));

        // The working agent keeps its identity.
        await service.ReportHeartbeatAsync(first.Credentials);
    }

    [Fact]
    public async Task RegisterAsync_ReusesOfflineDeviceWithSameHostName_AndInvalidatesOldKey()
    {
        var service = CreateService();
        var first = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));
        (await _devices.GetByIdAsync(first.Credentials.DeviceId))!.MarkOffline();

        var second = await service.RegisterAsync(new RegisterAgentRequest(Token, "pc-01", null));

        Assert.Equal(first.Credentials.DeviceId, second.Credentials.DeviceId);
        Assert.Single(await _devices.GetAllAsync());
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => service.ReportHeartbeatAsync(first.Credentials));
        await service.ReportHeartbeatAsync(second.Credentials);
    }

    [Fact]
    public async Task RenamedDevice_KeepsItsName_WhenItsAgentReportsAndRegistersAgain()
    {
        var service = CreateService();
        var first = await service.RegisterAsync(new RegisterAgentRequest(Token, "DESKTOP-TKKTAJ2", null));
        var device = (await _devices.GetByIdAsync(first.Credentials.DeviceId))!;
        device.Rename("Učebňa 3 - PC 1");

        await service.ReportHeartbeatAsync(first.Credentials);
        device.MarkOffline();
        var second = await service.RegisterAsync(new RegisterAgentRequest(Token, "DESKTOP-TKKTAJ2", null));

        Assert.Equal(first.Credentials.DeviceId, second.Credentials.DeviceId);
        Assert.Equal(("Učebňa 3 - PC 1", "DESKTOP-TKKTAJ2"), (device.Name, device.HostName));
    }

    [Fact]
    public async Task Agent_KeepsTheAddressOfItsDeviceCurrent_FromRegistrationOn()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(
            new RegisterAgentRequest(Token, "PC-01", null, System.Net.IPAddress.Parse("192.168.50.20")));
        var device = (await _devices.GetByIdAsync(registration.Credentials.DeviceId))!;
        Assert.Equal("192.168.50.20", device.IpAddress?.ToString());

        // The computer got another address from DHCP.
        await service.ReportHeartbeatAsync(registration.Credentials, networkAddress: System.Net.IPAddress.Parse("192.168.50.31"));
        Assert.Equal("192.168.50.31", device.IpAddress?.ToString());

        // A report without one keeps what is known.
        await service.ReportHeartbeatAsync(registration.Credentials);
        Assert.Equal("192.168.50.31", device.IpAddress?.ToString());
    }

    [Fact]
    public async Task ReportHeartbeatAsync_UpdatesLastSeen_AddsSnapshot_AndReturnsInterval()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await service.ReportHeartbeatAsync(registration.Credentials);

        var device = await _devices.GetByIdAsync(registration.Credentials.DeviceId);
        Assert.Equal(_time.GetUtcNow(), device!.LastSeenAt);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(120, result.SyncIntervalSeconds);
        Assert.Equal(2, _history.Snapshots.Count);
        // The device was online all along, so the heartbeat adds no event.
        Assert.Equal(2, _history.Events.Count);
    }

    [Fact]
    public async Task ReportHeartbeatAsync_FromOfflineDevice_EndsOutage_AndWritesOnlineEvent()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));
        var device = (await _devices.GetByIdAsync(registration.Credentials.DeviceId))!;
        new AvailabilityRecorder(_history).RecordOffline(device, _time.GetUtcNow());
        _time.Advance(TimeSpan.FromMinutes(10));

        await service.ReportHeartbeatAsync(registration.Credentials);

        var outage = Assert.Single(_history.Outages);
        Assert.Equal(_time.GetUtcNow(), outage.EndedAt);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(MonitoringEventType.DeviceWentOnline, _history.Events.Last().Type);
    }

    [Fact]
    public async Task ReportHeartbeatAsync_Throws_WhenKeyIsWrong()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));

        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => service.ReportHeartbeatAsync(registration.Credentials with { AgentKey = "wrong-key" }));
    }

    [Fact]
    public async Task ReportHeartbeatAsync_Throws_WhenDeviceIsUnknown()
    {
        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => CreateService().ReportHeartbeatAsync(new AgentCredentials(Guid.NewGuid(), "any-key")));
    }

    [Fact]
    public async Task ReportHeartbeatAsync_Throws_ForDeviceWithoutAgent()
    {
        var device = new Device(
            Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, System.Net.IPAddress.Loopback, _time.GetUtcNow());
        _devices.Add(device);

        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => CreateService().ReportHeartbeatAsync(new AgentCredentials(device.Id, string.Empty)));
    }

    [Fact]
    public async Task ReportHeartbeatAsync_TellsAgentToCaptureTraffic_AndStoresSummary_OnlyWhileSwitchedOn()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));
        var traffic = new TrafficReport(
            _time.GetUtcNow().AddSeconds(-60),
            _time.GetUtcNow(),
            [new ConversationReport(TrafficTransport.Udp, "DNS", "192.0.2.53", 53, 2, 2, 120, 300)]);

        var whileOff = await service.ReportHeartbeatAsync(registration.Credentials, traffic: traffic);

        Assert.False(whileOff.CaptureTraffic);
        Assert.Empty(_activity.TrafficRecords);

        (await _devices.GetByIdAsync(registration.Credentials.DeviceId))!.SetTrafficCapture(true);
        var whileOn = await service.ReportHeartbeatAsync(registration.Credentials, traffic: traffic);

        Assert.True(whileOn.CaptureTraffic);
        var record = Assert.Single(_activity.TrafficRecords);
        Assert.Equal(("DNS", "192.0.2.53", 53, 420L), (record.Protocol, record.RemoteAddress, record.ServicePort, record.BytesSent + record.BytesReceived));
    }

    [Fact]
    public async Task ReportHeartbeatAsync_RecordsWhyAgentCannotCapture_UntilItCanAgain()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));
        var device = (await _devices.GetByIdAsync(registration.Credentials.DeviceId))!;
        device.SetTrafficCapture(true);
        var now = _time.GetUtcNow();

        await service.ReportHeartbeatAsync(registration.Credentials, traffic: new TrafficReport(now, now, [], "TShark was not found."));
        Assert.Equal("TShark was not found.", device.TrafficCaptureProblem);

        await service.ReportHeartbeatAsync(registration.Credentials, traffic: new TrafficReport(now, now, []));
        Assert.Null(device.TrafficCaptureProblem);
    }

    private AgentService CreateService(string? configuredToken = Token)
    {
        return new AgentService(
            _devices,
            new FixedSettingsRepository(_settings),
            _history,
            new AvailabilityRecorder(_history),
            new DeviceActivityRecorder(_activity, _history),
            _activity,
            _unitOfWork,
            Options.Create(new AgentEnrollmentOptions { EnrollmentToken = configuredToken }),
            _time);
    }
}
