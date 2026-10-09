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

    public async Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Devices
            .OrderBy(device => device.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        _dbContext.Devices.Add(device);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
