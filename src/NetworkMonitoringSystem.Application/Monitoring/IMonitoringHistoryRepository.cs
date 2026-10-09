using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>
/// Storage for the history of devices: snapshots, outages and events.
/// Additions are saved through <see cref="Common.IUnitOfWork"/>.
/// </summary>
public interface IMonitoringHistoryRepository
{
    void AddSnapshot(DeviceSnapshot snapshot);

    void AddOutage(Outage outage);

    void AddEvent(MonitoringEvent monitoringEvent);

    /// <summary>Returns the most recent snapshot of the device that carries system resources, if there is one.</summary>
    Task<DeviceSnapshot?> GetLatestResourceSnapshotAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the outage of the device that has not ended yet, if there is one.</summary>
    Task<Outage?> GetOngoingOutageAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
