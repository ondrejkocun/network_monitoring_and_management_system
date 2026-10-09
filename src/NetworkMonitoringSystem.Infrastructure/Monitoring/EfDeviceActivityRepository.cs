using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Monitoring;

public sealed class EfDeviceActivityRepository : IDeviceActivityRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfDeviceActivityRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProcessRun>> GetRunningProcessesAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProcessRuns
            .Where(run => run.DeviceId == deviceId && run.EndedAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ListeningPort>> GetOpenPortsAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ListeningPorts
            .Include(port => port.ProcessRun)
            .Where(port => port.DeviceId == deviceId && port.ClosedAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveConnection>> GetConnectionsAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ActiveConnections
            .Include(connection => connection.ProcessRun)
            .Where(connection => connection.DeviceId == deviceId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessUsage>> GetLatestProcessUsagesAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var latestSnapshotId = await _dbContext.DeviceSnapshots
            .Where(snapshot => snapshot.DeviceId == deviceId && snapshot.ProcessUsages.Any())
            .OrderByDescending(snapshot => snapshot.RecordedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .Select(snapshot => (long?)snapshot.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSnapshotId is null)
        {
            return [];
        }

        return await _dbContext.Set<ProcessUsage>()
            .Include(usage => usage.ProcessRun)
            .Where(usage => EF.Property<long>(usage, "SnapshotId") == latestSnapshotId)
            .ToListAsync(cancellationToken);
    }

    public void AddProcessRun(ProcessRun processRun) => _dbContext.ProcessRuns.Add(processRun);

    public void AddListeningPort(ListeningPort port) => _dbContext.ListeningPorts.Add(port);

    public async Task ReplaceConnectionsAsync(
        Guid deviceId,
        IReadOnlyList<ActiveConnection> connections,
        CancellationToken cancellationToken = default)
    {
        // Loaded and removed through the context, so the replacement is saved together with the rest of the report.
        var existing = await _dbContext.ActiveConnections
            .Where(connection => connection.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        _dbContext.ActiveConnections.RemoveRange(existing);
        _dbContext.ActiveConnections.AddRange(connections);
    }
}
