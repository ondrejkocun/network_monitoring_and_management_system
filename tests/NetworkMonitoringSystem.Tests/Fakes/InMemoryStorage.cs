namespace NetworkMonitoringSystem.Tests.Fakes;

using System.Net;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Commands;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Commands;
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

    public Task<Device?> GetByIpAddressAsync(IPAddress ipAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_devices.FirstOrDefault(device => Equals(device.IpAddress, ipAddress)));
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

    /// <summary>Device and user names are not known here; the database test covers them.</summary>
    public Task<IReadOnlyList<EventLogEntryDto>> GetEventLogAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EventLogEntryDto> entries = Events
            .Where(monitoringEvent => monitoringEvent.OccurredAt >= from && monitoringEvent.OccurredAt <= to)
            .OrderByDescending(monitoringEvent => monitoringEvent.OccurredAt)
            .Take(limit)
            .Select(monitoringEvent => new EventLogEntryDto(
                monitoringEvent.OccurredAt, monitoringEvent.Type, monitoringEvent.Severity, monitoringEvent.Message, null, null))
            .ToList();

        return Task.FromResult(entries);
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

    public Task<IReadOnlyList<NetworkSample>> GetNetworkSamplesAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NetworkSample> samples = Snapshots
            .Where(snapshot => snapshot.DeviceId == deviceId && snapshot.RecordedAt >= from && snapshot.RecordedAt <= to)
            .OrderBy(snapshot => snapshot.RecordedAt)
            .Select(snapshot => new NetworkSample(
                snapshot.RecordedAt,
                snapshot.ResponseTimeMs,
                snapshot.NetworkInterfaces
                    .Select(networkInterface => new InterfaceCounter(networkInterface.Name, networkInterface.BytesSent, networkInterface.BytesReceived))
                    .ToList()))
            .ToList();

        return Task.FromResult(samples);
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

    public List<TrafficRecord> TrafficRecords { get; } = [];

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

    public Task<IReadOnlyList<ProcessRun>> GetProcessRunsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProcessRun> runs = ProcessRuns
            .Where(run => run.DeviceId == deviceId && run.StartedAt <= to && (run.EndedAt == null || run.EndedAt >= from))
            .OrderByDescending(run => run.StartedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult(runs);
    }

    public Task<IReadOnlyList<ListeningPort>> GetPortPeriodsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ListeningPort> ports = ListeningPorts
            .Where(port => port.DeviceId == deviceId && port.OpenedAt <= to && (port.ClosedAt == null || port.ClosedAt >= from))
            .OrderByDescending(port => port.OpenedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult(ports);
    }

    public void AddListeningPort(ListeningPort port) => ListeningPorts.Add(port);

    public void AddTrafficRecords(IEnumerable<TrafficRecord> records) => TrafficRecords.AddRange(records);

    public Task<IReadOnlyList<TrafficEntryDto>> GetTrafficAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxEntries,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TrafficEntryDto> entries = TrafficRecords
            .Where(record => record.DeviceId == deviceId && record.PeriodEnd > from && record.PeriodStart < to)
            .GroupBy(record => (record.Transport, record.Protocol, record.RemoteAddress, record.ServicePort))
            .Select(group => new TrafficEntryDto(
                group.Key.Transport,
                group.Key.Protocol,
                group.Key.RemoteAddress,
                group.Key.ServicePort,
                group.Sum(record => record.PacketsSent),
                group.Sum(record => record.PacketsReceived),
                group.Sum(record => record.BytesSent),
                group.Sum(record => record.BytesReceived)))
            .OrderByDescending(entry => entry.BytesSent + entry.BytesReceived)
            .Take(maxEntries)
            .ToList();

        return Task.FromResult(entries);
    }

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

    public int DiscardCount { get; private set; }

    /// <summary>How many of the next saves fail as if someone else had changed the same record.</summary>
    public int ConflictsToSimulate { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ConflictsToSimulate > 0)
        {
            ConflictsToSimulate--;

            throw new ConcurrencyConflictException("Simulated conflict.");
        }

        SaveCount++;

        return Task.CompletedTask;
    }

    public void DiscardChanges() => DiscardCount++;
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

    public Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<User>>(Users.OrderBy(user => user.UserName).ToList());
    }

    public Task<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Role>>(Roles.OrderBy(role => role.Name).ToList());
    }

    public Task<Role?> GetRoleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Roles.FirstOrDefault(role => role.Id == id));
    }

    public void AddUser(User user) => Users.Add(user);

    public void RemoveUser(User user) => Users.Remove(user);

    public void AddRole(Role role) => Roles.Add(role);

    public void RemoveRole(Role role)
    {
        Roles.Remove(role);

        foreach (var user in Users)
        {
            user.RemoveRole(role.Id);
        }
    }

    public Task<IReadOnlyList<UserSession>> GetSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<UserSession>>(Sessions.Where(session => session.UserId == userId).ToList());
    }

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

public sealed class InMemoryRemoteCommandRepository : IRemoteCommandRepository
{
    public List<RemoteCommand> Commands { get; } = [];

    public void Add(RemoteCommand command) => Commands.Add(command);

    public Task<RemoteCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Commands.FirstOrDefault(command => command.Id == id));
    }

    public Task<IReadOnlyList<RemoteCommand>> GetUnfinishedAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RemoteCommand>>(Commands
            .Where(command => command.DeviceId == deviceId && !command.IsFinished)
            .OrderBy(command => command.RequestedAt)
            .ToList());
    }

    public Task<IReadOnlyList<RemoteCommand>> GetRecentAsync(Guid deviceId, int limit, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RemoteCommand>>(Commands
            .Where(command => command.DeviceId == deviceId)
            .OrderByDescending(command => command.RequestedAt)
            .Take(limit)
            .ToList());
    }
}

public sealed class InMemoryFileTransferRepository(InMemoryRemoteCommandRepository commands) : IFileTransferRepository
{
    public List<FileTransfer> Transfers { get; } = [];

    public List<FileTransferChunk> Chunks { get; } = [];

    public void Add(FileTransfer transfer) => Transfers.Add(transfer);

    public void AddChunk(FileTransferChunk chunk) => Chunks.Add(chunk);

    public Task<FileTransfer?> GetAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Transfers.FirstOrDefault(transfer => transfer.CommandId == commandId));
    }

    public Task<byte[]?> GetChunkAsync(Guid commandId, int index, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Chunks.FirstOrDefault(chunk => chunk.CommandId == commandId && chunk.Index == index)?.Data);
    }

    public Task DeleteContentAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        Chunks.RemoveAll(chunk => chunk.CommandId == commandId);

        return Task.CompletedTask;
    }

    public Task<int> DeleteContentOfFinishedCommandsAsync(DateTimeOffset requestedBefore, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Chunks.RemoveAll(chunk => commands.Commands.Any(command =>
            command.Id == chunk.CommandId && (command.IsFinished || command.RequestedAt < requestedBefore))));
    }
}

public sealed class InMemoryEnrollmentCodeRepository : IEnrollmentCodeRepository
{
    public List<EnrollmentCode> Codes { get; } = [];

    public Task<EnrollmentCode?> GetByHashAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Codes.FirstOrDefault(code => code.CodeHash == codeHash));
    }

    public Task<EnrollmentCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Codes.FirstOrDefault(code => code.Id == id));
    }

    public Task<IReadOnlyList<EnrollmentCode>> GetCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<EnrollmentCode>>(Codes.Where(code => code.CreatedAt >= since).OrderByDescending(code => code.CreatedAt).ToList());
    }

    public void Add(EnrollmentCode code) => Codes.Add(code);
}
