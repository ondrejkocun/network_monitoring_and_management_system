using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>
/// Storage for what runs on devices: process runs, listening ports and connections.
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

    void AddProcessRun(ProcessRun processRun);

    void AddListeningPort(ListeningPort port);

    /// <summary>Replaces all stored connections of the device with the given ones.</summary>
    Task ReplaceConnectionsAsync(Guid deviceId, IReadOnlyList<ActiveConnection> connections, CancellationToken cancellationToken = default);
}
