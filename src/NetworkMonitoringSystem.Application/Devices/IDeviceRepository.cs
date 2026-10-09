using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds the device monitored by an agent with the given host name, ignoring case.</summary>
    Task<Device?> GetAgentDeviceByHostNameAsync(string hostName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Device device, CancellationToken cancellationToken = default);

    Task UpdateAsync(Device device, CancellationToken cancellationToken = default);
}
