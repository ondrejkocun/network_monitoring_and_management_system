namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class DeviceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly InMemoryDeviceActivityRepository _activity;
    private readonly DeviceService _service;

    public DeviceServiceTests()
    {
        _activity = new InMemoryDeviceActivityRepository(_history);
        _service = new DeviceService(
            new InMemoryDeviceRepository(),
            _history,
            _activity,
            _unitOfWork,
            new MutableTimeProvider(Now));
    }

    [Fact]
    public async Task RegisterDeviceAsync_StoresDeviceAndReturnsDto()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("  Server 01  ", MonitoringMode.Agent, "server-01.local"));

        var stored = await _service.GetDeviceAsync(registered.Id);

        Assert.NotEqual(Guid.Empty, registered.Id);
        Assert.Equal("Server 01", registered.Name);
        Assert.Equal(Now, registered.CreatedAt);
        Assert.Equal(DeviceStatus.Unknown, registered.Status);
        Assert.Equal(registered, stored);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RegisterDeviceAsync_RegistersDeviceWithoutAgentByIpAddress()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: " 192.168.50.1 "));

        Assert.Equal(MonitoringMode.Agentless, registered.MonitoringMode);
        Assert.Equal("192.168.50.1", registered.IpAddress);
        Assert.Null(registered.HostName);
    }

    [Fact]
    public async Task RegisterDeviceAsync_StoresTcpPort_OfDeviceWithoutAgent()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("NAS", MonitoringMode.Agentless, IpAddress: "192.168.50.5", TcpPort: 445));

        Assert.Equal(445, registered.TcpPort);
    }

    [Fact]
    public async Task RegisterDeviceAsync_Throws_WhenDeviceWithAgentHasTcpPort()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("PC", MonitoringMode.Agent, "pc-01", TcpPort: 3389)));

        Assert.Empty(await _service.GetDevicesAsync());
    }

    [Fact]
    public async Task RegisterDeviceAsync_Throws_WhenIpAddressAlreadyExists()
    {
        await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router Copy", MonitoringMode.Agentless, IpAddress: "192.168.50.1")));

        Assert.Contains("already exists", exception.Message);
        Assert.Single(await _service.GetDevicesAsync());
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RegisterDeviceAsync_Throws_WhenIpAddressIsInvalid()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "not-an-address")));

        Assert.Empty(await _service.GetDevicesAsync());
    }

    [Fact]
    public async Task RegisterDeviceAsync_Throws_WhenNameIsEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterDeviceAsync(
            new RegisterDeviceRequest(" ", MonitoringMode.Agent, "server-01.local")));

        Assert.Empty(await _service.GetDevicesAsync());
    }

    [Fact]
    public async Task GetDeviceAsync_ReturnsNull_WhenDeviceDoesNotExist()
    {
        var device = await _service.GetDeviceAsync(Guid.NewGuid());

        Assert.Null(device);
    }

    [Fact]
    public async Task RemoveDeviceAsync_RemovesDevice_AndWritesEvent()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));

        var removed = await _service.RemoveDeviceAsync(registered.Id);

        Assert.True(removed);
        Assert.Null(await _service.GetDeviceAsync(registered.Id));
        Assert.Equal(2, _unitOfWork.SaveCount);

        var removalEvent = Assert.Single(_history.Events);
        Assert.Equal(MonitoringEventType.DeviceRemoved, removalEvent.Type);
        Assert.Null(removalEvent.DeviceId);
        Assert.Contains("Router", removalEvent.Message);
    }

    [Fact]
    public async Task RemoveDeviceAsync_ReturnsFalse_WhenDeviceDoesNotExist()
    {
        var removed = await _service.RemoveDeviceAsync(Guid.NewGuid());

        Assert.False(removed);
        Assert.Empty(_history.Events);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task SetDeviceEnabledAsync_DisablesDevice_AndWritesEventWithItsAuthor()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));
        var userId = Guid.NewGuid();

        var disabled = await _service.SetDeviceEnabledAsync(registered.Id, isEnabled: false, userId);

        Assert.NotNull(disabled);
        Assert.False(disabled.IsEnabled);
        Assert.False((await _service.GetDeviceAsync(registered.Id))!.IsEnabled);
        Assert.Equal(2, _unitOfWork.SaveCount);

        var disabledEvent = Assert.Single(_history.Events);
        Assert.Equal(MonitoringEventType.DeviceDisabled, disabledEvent.Type);
        Assert.Equal(registered.Id, disabledEvent.DeviceId);
        Assert.Equal(userId, disabledEvent.UserId);
    }

    [Fact]
    public async Task SetDeviceEnabledAsync_EndsOngoingOutage_WhenDeviceIsDisabled()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));
        var outage = new Outage(registered.Id, Now.AddHours(-1));
        _history.AddOutage(outage);

        await _service.SetDeviceEnabledAsync(registered.Id, isEnabled: false);

        Assert.Equal(Now, outage.EndedAt);
    }

    [Fact]
    public async Task SetDeviceEnabledAsync_EnablesDisabledDevice_AndWritesEvent()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));
        await _service.SetDeviceEnabledAsync(registered.Id, isEnabled: false);

        var enabled = await _service.SetDeviceEnabledAsync(registered.Id, isEnabled: true);

        Assert.True(enabled!.IsEnabled);
        Assert.Equal(
            [MonitoringEventType.DeviceDisabled, MonitoringEventType.DeviceEnabled],
            _history.Events.Select(monitoringEvent => monitoringEvent.Type));
    }

    [Fact]
    public async Task SetDeviceEnabledAsync_ChangesNothing_WhenDeviceIsAlreadyInThatState()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));

        var enabled = await _service.SetDeviceEnabledAsync(registered.Id, isEnabled: true);

        Assert.True(enabled!.IsEnabled);
        Assert.Empty(_history.Events);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task SetDeviceEnabledAsync_ReturnsNull_WhenDeviceDoesNotExist()
    {
        Assert.Null(await _service.SetDeviceEnabledAsync(Guid.NewGuid(), isEnabled: false));
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RenameDeviceAsync_RenamesDevice_AndWritesEventWithOldAndNewNameAndTheAuthor()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));
        var userId = Guid.NewGuid();

        var renamed = await _service.RenameDeviceAsync(registered.Id, "  Hlavný router  ", userId);
        await _service.RenameDeviceAsync(registered.Id, "Hlavný router", userId);

        Assert.Equal("Hlavný router", renamed!.Name);
        var renameEvent = Assert.Single(_history.Events);
        Assert.Equal((MonitoringEventType.DeviceRenamed, registered.Id, userId), (renameEvent.Type, renameEvent.DeviceId, renameEvent.UserId));
        Assert.Equal("Device 'Router' was renamed to 'Hlavný router'.", renameEvent.Message);
    }

    [Fact]
    public async Task RenameDeviceAsync_RefusesEmptyOrTooLongName_AndIgnoresUnknownDevice()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenameDeviceAsync(registered.Id, "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenameDeviceAsync(registered.Id, new string('x', Device.MaxNameLength + 1)));
        Assert.Null(await _service.RenameDeviceAsync(Guid.NewGuid(), "Switch"));

        Assert.Equal("Router", (await _service.GetDeviceAsync(registered.Id))!.Name);
        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task SetTrafficCaptureAsync_SwitchesOnAndOff_WithEventsAndTheirAuthor()
    {
        var registered = await _service.RegisterDeviceAsync(new RegisterDeviceRequest("PC", MonitoringMode.Agent, "pc-01"));
        var userId = Guid.NewGuid();

        Assert.True((await _service.SetTrafficCaptureAsync(registered.Id, capture: true, userId))!.CaptureTraffic);
        Assert.True((await _service.SetTrafficCaptureAsync(registered.Id, capture: true, userId))!.CaptureTraffic);
        Assert.False((await _service.SetTrafficCaptureAsync(registered.Id, capture: false, userId))!.CaptureTraffic);

        // Switching on twice changes nothing the second time.
        Assert.Equal(2, _history.Events.Count);
        Assert.All(_history.Events, monitoringEvent =>
        {
            Assert.Equal(MonitoringEventType.TrafficCaptureChanged, monitoringEvent.Type);
            Assert.Equal(userId, monitoringEvent.UserId);
        });
    }

    [Fact]
    public async Task SetTrafficCaptureAsync_RefusesDeviceWithoutAgent_AndIgnoresUnknownDevice()
    {
        var router = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetTrafficCaptureAsync(router.Id, capture: true));
        Assert.Null(await _service.SetTrafficCaptureAsync(Guid.NewGuid(), capture: true));
        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task GetActivityHistoryAsync_ReturnsRunsAndPortsThatOverlapThePeriod_MostRecentFirst()
    {
        var deviceId = Guid.NewGuid();
        var endedBefore = Run(deviceId, 1, "old", Now.AddHours(-5), Now.AddHours(-3));
        var endedInside = Run(deviceId, 2, "backup", Now.AddHours(-4), Now.AddMinutes(-30));
        var stillRunning = Run(deviceId, 3, "sqlservr", Now.AddMinutes(-20), null);
        _activity.ProcessRuns.AddRange([endedBefore, endedInside, stillRunning, Run(Guid.NewGuid(), 4, "other device", Now.AddMinutes(-5), null)]);

        var openedAndClosed = new ListeningPort(deviceId, TransportProtocol.Tcp, "0.0.0.0", 8080, endedInside, Now.AddMinutes(-50));
        openedAndClosed.Close(Now.AddMinutes(-40));
        var closedBefore = new ListeningPort(deviceId, TransportProtocol.Udp, "0.0.0.0", 161, endedBefore, Now.AddHours(-5));
        closedBefore.Close(Now.AddHours(-4));
        _activity.ListeningPorts.AddRange([openedAndClosed, closedBefore]);

        var history = await _service.GetActivityHistoryAsync(deviceId, Now.AddHours(-1), Now);

        Assert.Equal(["sqlservr", "backup"], history.Processes.Select(run => run.Name));
        Assert.Null(history.Processes[0].EndedAt);
        var port = Assert.Single(history.Ports);
        Assert.Equal((8080, Now.AddMinutes(-40), "backup"), (port.Port, port.ClosedAt, port.Process!.Name));
        Assert.False(history.Truncated);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetActivityHistoryAsync(deviceId, Now, Now.AddSeconds(-1)));
    }

    [Fact]
    public async Task GetActivityHistoryAsync_ReturnsAtMostTheLimit_AndSaysSomethingWasLeftOut()
    {
        var deviceId = Guid.NewGuid();

        for (var pid = 0; pid < DeviceService.MaxActivityHistoryEntries + 1; pid++)
        {
            _activity.ProcessRuns.Add(Run(deviceId, pid, "short", Now.AddSeconds(-pid), Now.AddSeconds(-pid + 1)));
        }

        var history = await _service.GetActivityHistoryAsync(deviceId, Now.AddHours(-1), Now);

        Assert.Equal(DeviceService.MaxActivityHistoryEntries, history.Processes.Count);
        Assert.True(history.Truncated);
    }

    private static ProcessRun Run(Guid deviceId, int pid, string name, DateTimeOffset startedAt, DateTimeOffset? endedAt)
    {
        var run = new ProcessRun(deviceId, pid, name, startedAt);

        if (endedAt is { } ended)
        {
            run.End(ended);
        }

        return run;
    }

    [Fact]
    public async Task GetTrafficAsync_SumsSummariesThatOverlapThePeriod_LargestFirst()
    {
        var deviceId = Guid.NewGuid();
        _activity.AddTrafficRecords(
        [
            Traffic(deviceId, Now.AddMinutes(-2), "DNS", "192.168.50.1", 53, 300),
            Traffic(deviceId, Now.AddMinutes(-1), "DNS", "192.168.50.1", 53, 200),
            Traffic(deviceId, Now.AddMinutes(-1), "TLSv1.3", "160.79.104.10", 443, 9000),
            Traffic(deviceId, Now.AddHours(-3), "TLSv1.3", "160.79.104.10", 443, 1),
            Traffic(Guid.NewGuid(), Now.AddMinutes(-1), "HTTP", "192.0.2.80", 80, 5),
        ]);

        var entries = await _service.GetTrafficAsync(deviceId, Now.AddHours(-1), Now);

        Assert.Equal(["TLSv1.3", "DNS"], entries.Select(entry => entry.Protocol));
        Assert.Equal((500L, 2L), (entries[1].BytesReceived, entries[1].PacketsReceived));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetTrafficAsync(deviceId, Now, Now));
    }

    private static TrafficRecord Traffic(Guid deviceId, DateTimeOffset periodEnd, string protocol, string remote, int port, long bytesReceived)
    {
        return new TrafficRecord(deviceId, periodEnd.AddMinutes(-1), periodEnd, TrafficTransport.Tcp, protocol, remote, port, 1, 1, 10, bytesReceived);
    }

    [Fact]
    public async Task GetDevicesAsync_ReturnsAllRegisteredDevicesOrderedByName()
    {
        await _service.RegisterDeviceAsync(new RegisterDeviceRequest("Switch", MonitoringMode.Agent, "switch.local"));
        await _service.RegisterDeviceAsync(new RegisterDeviceRequest("Router", MonitoringMode.Agent, "router.local"));

        var devices = await _service.GetDevicesAsync();

        Assert.Equal(["Router", "Switch"], devices.Select(device => device.Name));
    }
}
