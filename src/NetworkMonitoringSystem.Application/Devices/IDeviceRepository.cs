using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

/// <summary>
/// Access to stored devices. Changes to a loaded or added device are saved through <see cref="Common.IUnitOfWork"/>.
/// </summary>
public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds the device monitored by an agent with the given host name, ignoring case.</summary>
    Task<Device?> GetAgentDeviceByHostNameAsync(string hostName, CancellationToken cancellationToken = default);

    /// <summary>Returns enabled, online devices with an agent that have not reported since the given time.</summary>
    Task<IReadOnlyList<Device>> GetOnlineAgentDevicesNotSeenSinceAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(Device device);
}
