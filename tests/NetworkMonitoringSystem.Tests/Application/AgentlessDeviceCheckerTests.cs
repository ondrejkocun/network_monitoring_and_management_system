namespace NetworkMonitoringSystem.Tests.Application;

using System.Net;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class AgentlessDeviceCheckerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly IPAddress RouterAddress = IPAddress.Parse("192.168.50.1");

    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly MutableTimeProvider _time = new(Start);
    private readonly FakeProbe _probe = new();

    // 60 s interval and 3 missed synchronizations: a device is offline after 180 s without an answer.
    private readonly MonitoringSettings _settings = new(syncIntervalSeconds: 60, offlineAfterMissedSyncs: 3);

    [Fact]
    public async Task CheckAllAsync_ReturnsGlobalInterval_EvenWithoutDevices()
    {
        var nextCheckIn = await CreateChecker().CheckAllAsync();

        Assert.Equal(TimeSpan.FromSeconds(60), nextCheckIn);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task CheckAllAsync_ReachableDevice_GoesOnline_WithSnapshotAndEvent()
    {
        var router = AddAgentlessDevice("Router", RouterAddress);
        _probe.Answer(RouterAddress, responseTimeMs: 12);

        await CreateChecker().CheckAllAsync();

        Assert.Equal(DeviceStatus.Online, router.Status);
        Assert.Equal(Start, router.LastSeenAt);

        var snapshot = Assert.Single(_history.Snapshots);
        Assert.Equal(DeviceStatus.Online, snapshot.Status);
        Assert.Equal(12, snapshot.ResponseTimeMs);

        Assert.Equal(MonitoringEventType.DeviceWentOnline, Assert.Single(_history.Events).Type);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task CheckAllAsync_DeviceThatNeverAnswered_GoesOfflineOnFirstFailure()
    {
        var router = AddAgentlessDevice("Router", RouterAddress);

        await CreateChecker().CheckAllAsync();

        Assert.Equal(DeviceStatus.Offline, router.Status);
        Assert.Equal(Start, Assert.Single(_history.Outages).StartedAt);
        Assert.Equal(DeviceStatus.Offline, Assert.Single(_history.Snapshots).Status);
    }

    [Fact]
    public async Task OnlineDevice_SurvivesFailedChecksWithinTolerance_ThenGoesOffline_AndComesBack()
    {
        var router = AddAgentlessDevice("Router", RouterAddress);
        var checker = CreateChecker();
        var monitor = CreateMonitor();
        _probe.Answer(RouterAddress, responseTimeMs: 5);
        await checker.CheckAllAsync();

        // Three failed checks, one interval apart: the last successful contact is exactly 180 s old.
        _probe.StopAnswering(RouterAddress);

        for (var i = 0; i < 3; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(60));
            await checker.CheckAllAsync();
            await monitor.EvaluateAsync();
        }

        Assert.Equal(DeviceStatus.Online, router.Status);
        Assert.Empty(_history.Outages);

        // The fourth failed check exceeds the tolerance.
        _time.Advance(TimeSpan.FromSeconds(60));
        await checker.CheckAllAsync();
        await monitor.EvaluateAsync();

        Assert.Equal(DeviceStatus.Offline, router.Status);
        var outage = Assert.Single(_history.Outages);
        Assert.Equal(Start, outage.StartedAt);
        Assert.True(outage.IsOngoing);

        // The device answers again.
        _probe.Answer(RouterAddress, responseTimeMs: 7);
        _time.Advance(TimeSpan.FromSeconds(60));
        await checker.CheckAllAsync();

        Assert.Equal(DeviceStatus.Online, router.Status);
        Assert.Equal(_time.GetUtcNow(), outage.EndedAt);
        Assert.Equal(
            [MonitoringEventType.DeviceWentOnline, MonitoringEventType.DeviceWentOffline, MonitoringEventType.DeviceWentOnline],
            _history.Events.Select(monitoringEvent => monitoringEvent.Type));
        // One snapshot per check: 1 successful, 4 failed, 1 successful.
        Assert.Equal(6, _history.Snapshots.Count);
    }

    [Fact]
    public async Task CheckAllAsync_ChecksOnlyDevicesWithoutAgent()
    {
        var agentDevice = new Device(Guid.NewGuid(), "PC-01", MonitoringMode.Agent, "pc-01", IPAddress.Parse("192.168.50.20"), Start);
        _devices.Add(agentDevice);
        AddAgentlessDevice("Router", RouterAddress);

        await CreateChecker().CheckAllAsync();

        Assert.Equal([RouterAddress], _probe.ProbedAddresses);
        Assert.Equal(DeviceStatus.Unknown, agentDevice.Status);
    }

    [Fact]
    public async Task CheckAllAsync_RecordsEachDeviceSeparately()
    {
        var switchAddress = IPAddress.Parse("192.168.50.2");
        var router = AddAgentlessDevice("Router", RouterAddress);
        var networkSwitch = AddAgentlessDevice("Switch", switchAddress);
        _probe.Answer(switchAddress, responseTimeMs: 3);

        await CreateChecker().CheckAllAsync();

        Assert.Equal(DeviceStatus.Offline, router.Status);
        Assert.Equal(DeviceStatus.Online, networkSwitch.Status);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    private Device AddAgentlessDevice(string name, IPAddress address)
    {
        var device = new Device(Guid.NewGuid(), name, MonitoringMode.Agentless, null, address, _time.GetUtcNow());
        _devices.Add(device);

        return device;
    }

    private AgentlessDeviceChecker CreateChecker()
    {
        return new AgentlessDeviceChecker(
            _devices,
            new FixedSettingsRepository(_settings),
            _probe,
            new AvailabilityRecorder(_history),
            _unitOfWork,
            _time);
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

    private sealed class FakeProbe : IDeviceReachabilityProbe
    {
        private readonly Dictionary<IPAddress, int> _answering = [];

        public List<IPAddress> ProbedAddresses { get; } = [];

        public void Answer(IPAddress address, int responseTimeMs) => _answering[address] = responseTimeMs;

        public void StopAnswering(IPAddress address) => _answering.Remove(address);

        public Task<ReachabilityResult> ProbeAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            lock (ProbedAddresses)
            {
                ProbedAddresses.Add(address);
            }

            return Task.FromResult(_answering.TryGetValue(address, out var responseTimeMs)
                ? ReachabilityResult.Reachable(responseTimeMs)
                : ReachabilityResult.Unreachable);
        }
    }
}
