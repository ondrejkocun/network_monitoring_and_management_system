using System.Collections.Concurrent;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Infrastructure.Devices;

/// <summary>
/// Temporary repository that keeps devices in memory until the database layer exists.
/// </summary>
public sealed class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly ConcurrentDictionary<Guid, Device> _devices = new();

    public Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _devices.TryGetValue(id, out var device);

        return Task.FromResult(device);
    }

    public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Device> devices = _devices.Values.OrderBy(device => device.Name).ToList();

        return Task.FromResult(devices);
    }

    public Task AddAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_devices.TryAdd(device.Id, device))
        {
            throw new InvalidOperationException($"Device with identifier '{device.Id}' already exists.");
        }

        return Task.CompletedTask;
    }
}
