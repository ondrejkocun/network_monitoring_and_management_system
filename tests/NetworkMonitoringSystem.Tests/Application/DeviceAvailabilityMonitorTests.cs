namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class DeviceAvailabilityMonitorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly MutableTimeProvider _time = new(Start);

    // 60 s interval and 3 missed synchronizations: a device is offline after 180 s of silence.
    private readonly MonitoringSettings _settings = new(syncIntervalSeconds: 60, offlineAfterMissedSyncs: 3);

    [Fact]
    public async Task GetOfflineThresholdAsync_IsIntervalTimesAllowedMisses()
    {
        Assert.Equal(TimeSpan.FromSeconds(180), await CreateMonitor().GetOfflineThresholdAsync());
    }

    [Fact]
    public async Task GetStartupGracePeriodAsync_IsOneIntervalPlusMargin_NotTheWholeThreshold()
    {
        var gracePeriod = await CreateMonitor().GetStartupGracePeriodAsync();

        Assert.Equal(TimeSpan.FromSeconds(80), gracePeriod);
        Assert.True(gracePeriod < await CreateMonitor().GetOfflineThresholdAsync());
    }

    [Fact]
    public async Task EvaluateAsync_KeepsDeviceOnline_UntilThresholdIsExceeded()
    {
        var device = AddOnlineAgentDevice("PC-01");
        _time.Advance(TimeSpan.FromSeconds(180));

        var wentOffline = await CreateMonitor().EvaluateAsync();

        Assert.Equal(0, wentOffline);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Empty(_history.Outages);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task EvaluateAsync_MarksSilentDeviceOffline_WithOutageAndEvent()
    {
        var device = AddOnlineAgentDevice("PC-01");
        _time.Advance(TimeSpan.FromSeconds(181));

        var wentOffline = await CreateMonitor().EvaluateAsync();

        Assert.Equal(1, wentOffline);
        Assert.Equal(DeviceStatus.Offline, device.Status);
        Assert.Equal(1, _unitOfWork.SaveCount);

        var outage = Assert.Single(_history.Outages);
        Assert.Equal(device.Id, outage.DeviceId);
        Assert.Equal(Start, outage.StartedAt);
        Assert.True(outage.IsOngoing);

        var offlineEvent = Assert.Single(_history.Events);
        Assert.Equal(MonitoringEventType.DeviceWentOffline, offlineEvent.Type);
        Assert.Equal(EventSeverity.Warning, offlineEvent.Severity);
        Assert.Equal(_time.GetUtcNow(), offlineEvent.OccurredAt);
        Assert.Equal(device.Id, offlineEvent.DeviceId);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotReportTheSameOutageTwice()
    {
        AddOnlineAgentDevice("PC-01");
        _time.Advance(TimeSpan.FromMinutes(10));
        var monitor = CreateMonitor();
        await monitor.EvaluateAsync();
        _time.Advance(TimeSpan.FromMinutes(10));

        var wentOffline = await monitor.EvaluateAsync();

        Assert.Equal(0, wentOffline);
        Assert.Single(_history.Outages);
        Assert.Single(_history.Events);
    }

    [Fact]
    public async Task EvaluateAsync_OnlyAffectsSilentDevices()
    {
        var silent = AddOnlineAgentDevice("PC-01");
        _time.Advance(TimeSpan.FromMinutes(10));
        var reporting = AddOnlineAgentDevice("PC-02");

        await CreateMonitor().EvaluateAsync();

        Assert.Equal(DeviceStatus.Offline, silent.Status);
        Assert.Equal(DeviceStatus.Online, reporting.Status);
    }

    [Fact]
    public async Task EvaluateAsync_AlsoMarksSilentDeviceWithoutAgentOffline()
    {
        var agentless = new Device(
            Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, System.Net.IPAddress.Loopback, Start);
        agentless.RecordContact(Start);
        _devices.Add(agentless);
        _time.Advance(TimeSpan.FromHours(1));

        var wentOffline = await CreateMonitor().EvaluateAsync();

        Assert.Equal(1, wentOffline);
        Assert.Equal(DeviceStatus.Offline, agentless.Status);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresDevicesNeverSeen()
    {
        var neverSeen = new Device(Guid.NewGuid(), "PC-03", MonitoringMode.Agent, "pc-03", null, Start);
        _devices.Add(neverSeen);
        _time.Advance(TimeSpan.FromHours(1));

        var wentOffline = await CreateMonitor().EvaluateAsync();

        Assert.Equal(0, wentOffline);
        Assert.Equal(DeviceStatus.Unknown, neverSeen.Status);
    }

    [Fact]
    public async Task EvaluateAsync_UsesCurrentSettings()
    {
        var device = AddOnlineAgentDevice("PC-01");
        _time.Advance(TimeSpan.FromSeconds(31));
        _settings.ChangeSyncInterval(10);

        await CreateMonitor().EvaluateAsync();

        Assert.Equal(DeviceStatus.Offline, device.Status);
    }

    private Device AddOnlineAgentDevice(string hostName)
    {
        var device = new Device(Guid.NewGuid(), hostName, MonitoringMode.Agent, hostName, null, _time.GetUtcNow());
        device.RecordContact(_time.GetUtcNow());
        _devices.Add(device);

        return device;
    }

    private DeviceAvailabilityMonitor CreateMonitor()
    {
        return new DeviceAvailabilityMonitor(
            _devices,
            new FixedSettingsRepository(_settings),
            new AvailabilityRecorder(_history),
            _unitOfWork,
            _time);
    }
}
