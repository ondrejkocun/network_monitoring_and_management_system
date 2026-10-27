using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>
/// Storage for what runs on devices: process runs, listening ports, connections and summaries of their traffic.
/// Additions and changes are saved through <see cref="Common.IUnitOfWork"/> unless stated otherwise.
/// </summary>
public interface IDeviceActivityRepository
{
    /// <summary>Returns the process runs of the device that have not ended.</summary>
    Task<IReadOnlyList<ProcessRun>> GetRunningProcessesAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the ports of the device that have not been closed, with their processes.</summary>
    Task<IReadOnlyList<ListeningPort>> GetOpenPortsAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the connections the device had when it last reported, with their processes.</summary>
    Task<IReadOnlyList<ActiveConnection>> GetConnectionsAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the usage recorded in the most recent snapshot of the device that has any.</summary>
    Task<IReadOnlyList<ProcessUsage>> GetLatestProcessUsagesAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the process runs of the device that overlap the period (started before it ended, and not ended before
    /// it started), the most recently started first.
    /// </summary>
    Task<IReadOnlyList<ProcessRun>> GetProcessRunsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the periods in which ports of the device were open that overlap the period, with their processes,
    /// the most recently opened first.
    /// </summary>
    Task<IReadOnlyList<ListeningPort>> GetPortPeriodsAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken = default);

    void AddProcessRun(ProcessRun processRun);

    void AddListeningPort(ListeningPort port);

    void AddTrafficRecords(IEnumerable<TrafficRecord> records);

    /// <summary>
    /// Returns the traffic of the device in summaries that overlap the period, summed per transport, protocol, remote
    /// address and service port, the largest first. A summary partly inside the period counts whole.
    /// </summary>
    Task<IReadOnlyList<Devices.TrafficEntryDto>> GetTrafficAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxEntries,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces all stored connections of the device with the given ones.</summary>
    Task ReplaceConnectionsAsync(Guid deviceId, IReadOnlyList<ActiveConnection> connections, CancellationToken cancellationToken = default);
}
