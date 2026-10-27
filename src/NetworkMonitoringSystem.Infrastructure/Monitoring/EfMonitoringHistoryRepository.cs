using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Monitoring;

public sealed class EfMonitoringHistoryRepository : IMonitoringHistoryRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfMonitoringHistoryRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public void AddSnapshot(DeviceSnapshot snapshot) => _dbContext.DeviceSnapshots.Add(snapshot);

    public void AddOutage(Outage outage) => _dbContext.Outages.Add(outage);

    public void AddEvent(MonitoringEvent monitoringEvent) => _dbContext.Events.Add(monitoringEvent);

    public Task<DeviceSnapshot?> GetLatestResourceSnapshotAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return _dbContext.DeviceSnapshots
            .AsNoTracking()
            .Include(snapshot => snapshot.Disks)
            .Include(snapshot => snapshot.NetworkInterfaces)
            .Where(snapshot => snapshot.DeviceId == deviceId && snapshot.HasResources)
            .OrderByDescending(snapshot => snapshot.RecordedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<HistoryCleanupResult> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        // Each statement runs directly in the database; disks and interfaces of a snapshot go with it by cascade.
        var snapshots = await _dbContext.DeviceSnapshots
            .Where(snapshot => snapshot.RecordedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var outages = await _dbContext.Outages
            .Where(outage => outage.EndedAt != null && outage.EndedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var events = await _dbContext.Events
            .Where(monitoringEvent => monitoringEvent.OccurredAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var processRuns = await _dbContext.ProcessRuns
            .Where(run => run.EndedAt != null && run.EndedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var listeningPorts = await _dbContext.ListeningPorts
            .Where(port => port.ClosedAt != null && port.ClosedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var trafficRecords = await _dbContext.TrafficRecords
            .Where(record => record.PeriodEnd < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return new HistoryCleanupResult(snapshots, outages, events, processRuns, listeningPorts, trafficRecords);
    }

    public async Task<IReadOnlyList<Outage>> GetOutagesAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Outages
            .AsNoTracking()
            .Where(outage => outage.DeviceId == deviceId
                && outage.StartedAt < to
                && (outage.EndedAt == null || outage.EndedAt > from))
            .OrderByDescending(outage => outage.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MonitoringEvent>> GetEventsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .Where(monitoringEvent => monitoringEvent.DeviceId == deviceId
                && monitoringEvent.OccurredAt >= from
                && monitoringEvent.OccurredAt <= to)
            .OrderByDescending(monitoringEvent => monitoringEvent.OccurredAt)
            .ThenByDescending(monitoringEvent => monitoringEvent.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventLogEntryDto>> GetEventLogAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // A removed device or user leaves its events behind; the name is then simply not known.
        var rows = await (
                from monitoringEvent in _dbContext.Events.AsNoTracking()
                where monitoringEvent.OccurredAt >= @from && monitoringEvent.OccurredAt <= to
                join device in _dbContext.Devices on monitoringEvent.DeviceId equals (Guid?)device.Id into devices
                from device in devices.DefaultIfEmpty()
                join user in _dbContext.Users on monitoringEvent.UserId equals (Guid?)user.Id into users
                from user in users.DefaultIfEmpty()
                orderby monitoringEvent.OccurredAt descending, monitoringEvent.Id descending
                select new
                {
                    monitoringEvent.OccurredAt,
                    monitoringEvent.Type,
                    monitoringEvent.Severity,
                    monitoringEvent.Message,
                    DeviceName = device == null ? null : device.Name,
                    UserName = user == null ? null : user.UserName,
                })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new EventLogEntryDto(row.OccurredAt, row.Type, row.Severity, row.Message, row.DeviceName, row.UserName))
            .ToList();
    }

    public async Task<IReadOnlyList<NetworkSample>> GetNetworkSamplesAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.DeviceSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.DeviceId == deviceId
                && snapshot.RecordedAt >= from
                && snapshot.RecordedAt <= to)
            .OrderBy(snapshot => snapshot.RecordedAt)
            .Select(snapshot => new
            {
                snapshot.RecordedAt,
                snapshot.ResponseTimeMs,
                Interfaces = snapshot.NetworkInterfaces
                    .Select(networkInterface => new { networkInterface.Name, networkInterface.BytesSent, networkInterface.BytesReceived })
                    .ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new NetworkSample(
                row.RecordedAt,
                row.ResponseTimeMs,
                row.Interfaces.Select(counter => new InterfaceCounter(counter.Name, counter.BytesSent, counter.BytesReceived)).ToList()))
            .ToList();
    }

    public Task<Outage?> GetOngoingOutageAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Outages
            .Where(outage => outage.DeviceId == deviceId && outage.EndedAt == null)
            .OrderByDescending(outage => outage.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
