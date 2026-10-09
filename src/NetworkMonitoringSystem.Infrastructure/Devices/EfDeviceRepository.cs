using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Devices;

public sealed class EfDeviceRepository : IDeviceRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfDeviceRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Devices.FirstOrDefaultAsync(device => device.Id == id, cancellationToken);
    }

    public Task<Device?> GetAgentDeviceByHostNameAsync(string hostName, CancellationToken cancellationToken = default)
    {
        var normalized = hostName.ToLower();

        return _dbContext.Devices.FirstOrDefaultAsync(
            device => device.MonitoringMode == MonitoringMode.Agent
                && device.HostName != null
                && device.HostName.ToLower() == normalized,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Device>> GetOnlineDevicesNotSeenSinceAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Devices
            .Where(device => device.IsEnabled
                && device.Status == DeviceStatus.Online
                && device.LastSeenAt < threshold)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Device>> GetEnabledAgentlessDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Devices
            .Where(device => device.MonitoringMode == MonitoringMode.Agentless && device.IsEnabled)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Devices
            .OrderBy(device => device.Name)
            .ToListAsync(cancellationToken);
    }

    public void Add(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _dbContext.Devices.Add(device);
    }

    public void Remove(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Snapshots and outages of the device are deleted by the database; events only lose the link to it.
        _dbContext.Devices.Remove(device);
    }
}
