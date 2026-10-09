using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Monitoring;

public sealed class EfMonitoringSettingsRepository : IMonitoringSettingsRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfMonitoringSettingsRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task<MonitoringSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        // The row is inserted by the initial migration, so it always exists.
        return _dbContext.MonitoringSettings.SingleAsync(cancellationToken);
    }
}
