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

    public Task<Outage?> GetOngoingOutageAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Outages
            .Where(outage => outage.DeviceId == deviceId && outage.EndedAt == null)
            .OrderByDescending(outage => outage.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
