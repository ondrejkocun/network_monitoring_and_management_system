namespace NetworkMonitoringSystem.Tests.Fakes;

using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Access;
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

    public Task<DeviceSnapshot?> GetLatestResourceSnapshotAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Snapshots.LastOrDefault(snapshot => snapshot.DeviceId == deviceId && snapshot.HasResources));
    }

    public Task<HistoryCleanupResult> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var result = new HistoryCleanupResult(
            Snapshots.RemoveAll(snapshot => snapshot.RecordedAt < cutoff),
            Outages.RemoveAll(outage => outage.EndedAt < cutoff),
            Events.RemoveAll(monitoringEvent => monitoringEvent.OccurredAt < cutoff));

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Outage>> GetOutagesAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Outage> outages = Outages
            .Where(outage => outage.DeviceId == deviceId && outage.StartedAt < to && (outage.EndedAt is null || outage.EndedAt > from))
            .OrderByDescending(outage => outage.StartedAt)
            .ToList();

        return Task.FromResult(outages);
    }

    public Task<IReadOnlyList<MonitoringEvent>> GetEventsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MonitoringEvent> events = Events
            .Where(monitoringEvent => monitoringEvent.DeviceId == deviceId && monitoringEvent.OccurredAt >= from && monitoringEvent.OccurredAt <= to)
            .OrderByDescending(monitoringEvent => monitoringEvent.OccurredAt)
            .Take(limit)
            .ToList();

        return Task.FromResult(events);
    }

    public Task<IReadOnlyList<ResourcePoint>> GetResourcePointsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ResourcePoint> points = Snapshots
            .Where(snapshot => snapshot.DeviceId == deviceId && snapshot.HasResources && snapshot.RecordedAt >= from && snapshot.RecordedAt <= to)
            .OrderBy(snapshot => snapshot.RecordedAt)
            .Select(snapshot => new ResourcePoint(
                snapshot.RecordedAt,
                snapshot.CpuUsagePercent,
                snapshot.MemoryUsedBytes ?? 0,
                snapshot.MemoryTotalBytes ?? 0))
            .ToList();

        return Task.FromResult(points);
    }

    public Task<Outage?> GetOngoingOutageAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Outages.LastOrDefault(outage => outage.DeviceId == deviceId && outage.IsOngoing));
    }
}

public sealed class InMemoryDeviceActivityRepository : IDeviceActivityRepository
{
    private readonly InMemoryMonitoringHistoryRepository? _history;

    /// <param name="history">Where snapshots are stored, to look up the latest process usage in.</param>
    public InMemoryDeviceActivityRepository(InMemoryMonitoringHistoryRepository? history = null)
    {
        _history = history;
    }

    public List<ProcessRun> ProcessRuns { get; } = [];

    public List<ListeningPort> ListeningPorts { get; } = [];

    public List<ActiveConnection> Connections { get; } = [];

    public Task<IReadOnlyList<ProcessRun>> GetRunningProcessesAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ProcessRun>>(ProcessRuns.Where(run => run.DeviceId == deviceId && run.IsRunning).ToList());
    }

    public Task<IReadOnlyList<ListeningPort>> GetOpenPortsAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ListeningPort>>(ListeningPorts.Where(port => port.DeviceId == deviceId && port.IsOpen).ToList());
    }

    public Task<IReadOnlyList<ActiveConnection>> GetConnectionsAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ActiveConnection>>(Connections.Where(connection => connection.DeviceId == deviceId).ToList());
    }

    public Task<IReadOnlyList<ProcessUsage>> GetLatestProcessUsagesAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var latest = _history?.Snapshots.LastOrDefault(snapshot => snapshot.DeviceId == deviceId && snapshot.ProcessUsages.Count > 0);

        return Task.FromResult(latest?.ProcessUsages ?? []);
    }

    public void AddProcessRun(ProcessRun processRun) => ProcessRuns.Add(processRun);

    public void AddListeningPort(ListeningPort port) => ListeningPorts.Add(port);

    public Task ReplaceConnectionsAsync(Guid deviceId, IReadOnlyList<ActiveConnection> connections, CancellationToken cancellationToken = default)
    {
        Connections.RemoveAll(connection => connection.DeviceId == deviceId);
        Connections.AddRange(connections);

        return Task.CompletedTask;
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

public sealed class InMemoryUserRepository : IUserRepository
{
    public List<User> Users { get; } = [];

    public List<Role> Roles { get; } = [];

    public List<UserSession> Sessions { get; } = [];

    public Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Users.Count > 0);

    public Task<User?> GetUserByNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Users.FirstOrDefault(user => user.UserName == userName));
    }

    public Task<User?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Users.FirstOrDefault(user => user.Id == id));
    }

    public Task<Role?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Roles.FirstOrDefault(role => role.Name == name));
    }

    public void AddUser(User user) => Users.Add(user);

    public void AddRole(Role role) => Roles.Add(role);

    public Task<UserSession?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Sessions.FirstOrDefault(session => session.TokenHash == tokenHash));
    }

    public void AddSession(UserSession session) => Sessions.Add(session);

    public void RemoveSession(UserSession session) => Sessions.Remove(session);

    public Task<int> DeleteSessionsExpiredBeforeAsync(DateTimeOffset time, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Sessions.RemoveAll(session => session.ExpiresAt < time));
    }
}
