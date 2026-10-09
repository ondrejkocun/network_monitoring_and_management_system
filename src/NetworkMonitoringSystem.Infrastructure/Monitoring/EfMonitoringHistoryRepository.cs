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

        return new HistoryCleanupResult(snapshots, outages, events);
    }

    public Task<Outage?> GetOngoingOutageAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Outages
            .Where(outage => outage.DeviceId == deviceId && outage.EndedAt == null)
            .OrderByDescending(outage => outage.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
