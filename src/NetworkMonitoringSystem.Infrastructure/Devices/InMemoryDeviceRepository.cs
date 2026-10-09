using System.Collections.Concurrent;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Infrastructure.Devices;

/// <summary>
/// Repository that keeps devices in memory. Used where a database is not wanted, such as unit tests.
/// </summary>
public sealed class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly ConcurrentDictionary<Guid, Device> _devices = new();

    public Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _devices.TryGetValue(id, out var device);

        return Task.FromResult(device);
    }

    public Task<Device?> GetAgentDeviceByHostNameAsync(string hostName, CancellationToken cancellationToken = default)
    {
        var device = _devices.Values.FirstOrDefault(device =>
            device.MonitoringMode == MonitoringMode.Agent
            && string.Equals(device.HostName, hostName, StringComparison.OrdinalIgnoreCase));

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

    public Task UpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!_devices.ContainsKey(device.Id))
        {
            throw new InvalidOperationException($"Device with identifier '{device.Id}' does not exist.");
        }

        _devices[device.Id] = device;

        return Task.CompletedTask;
    }
}
