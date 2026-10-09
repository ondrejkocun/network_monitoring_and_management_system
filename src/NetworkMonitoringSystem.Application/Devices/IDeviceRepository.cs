using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Device device, CancellationToken cancellationToken = default);
}
