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

    /// <summary>
    /// Deletes snapshots recorded, events occurred, and outages, process runs and port periods ended before
    /// the given time, immediately and without <see cref="Common.IUnitOfWork"/>. What is still in progress is kept.
    /// </summary>
    Task<HistoryCleanupResult> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>Returns the outages of the device that overlap the period, the most recent first.</summary>
    Task<IReadOnlyList<Outage>> GetOutagesAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Returns at most <paramref name="limit"/> events of the device in the period, the most recent first.</summary>
    Task<IReadOnlyList<MonitoringEvent>> GetEventsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Returns processor and memory use from the device's snapshots in the period, the oldest first.</summary>
    Task<IReadOnlyList<ResourcePoint>> GetResourcePointsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the outage of the device that has not ended yet, if there is one.</summary>
    Task<Outage?> GetOngoingOutageAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
