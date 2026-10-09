namespace NetworkMonitoringSystem.Tests.Fakes;

using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>In-memory stand-ins for the database, used by unit tests of the application layer.</summary>
public sealed class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly List<Device> _devices = [];

    public Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_devices.FirstOrDefault(device => device.Id == id));
    }

    public Task<Device?> GetAgentDeviceByHostNameAsync(string hostName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_devices.FirstOrDefault(device =>
            device.MonitoringMode == MonitoringMode.Agent
            && string.Equals(device.HostName, hostName, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<IReadOnlyList<Device>> GetEnabledAgentlessDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Device> devices = _devices
            .Where(device => device.MonitoringMode == MonitoringMode.Agentless && device.IsEnabled)
            .ToList();

        return Task.FromResult(devices);
    }

    public Task<IReadOnlyList<Device>> GetOnlineDevicesNotSeenSinceAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Device> devices = _devices
            .Where(device => device.IsEnabled
                && device.Status == DeviceStatus.Online
                && device.LastSeenAt < threshold)
            .ToList();

        return Task.FromResult(devices);
    }

    public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Device> devices = _devices.OrderBy(device => device.Name).ToList();

        return Task.FromResult(devices);
    }

    public void Add(Device device) => _devices.Add(device);

    public void Remove(Device device) => _devices.Remove(device);
}

public sealed class InMemoryMonitoringHistoryRepository : IMonitoringHistoryRepository
{
    public List<DeviceSnapshot> Snapshots { get; } = [];

    public List<Outage> Outages { get; } = [];

    public List<MonitoringEvent> Events { get; } = [];

    public void AddSnapshot(DeviceSnapshot snapshot) => Snapshots.Add(snapshot);

    public void AddOutage(Outage outage) => Outages.Add(outage);

    public void AddEvent(MonitoringEvent monitoringEvent) => Events.Add(monitoringEvent);

    public Task<Outage?> GetOngoingOutageAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Outages.LastOrDefault(outage => outage.DeviceId == deviceId && outage.IsOngoing));
    }
}

public sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;

        return Task.CompletedTask;
    }
}

public sealed class FixedSettingsRepository(MonitoringSettings settings) : IMonitoringSettingsRepository
{
    public Task<MonitoringSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
}

public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
