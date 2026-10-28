using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Devices;

public sealed class EfFirewallRuleRepository : IFirewallRuleRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfFirewallRuleRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FirewallRule>> GetByDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FirewallRules
            .Where(rule => rule.DeviceId == deviceId)
            .OrderBy(rule => rule.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<FirewallRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.FirewallRules.FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);
    }

    public void Add(FirewallRule rule) => _dbContext.FirewallRules.Add(rule);

    public void Remove(FirewallRule rule) => _dbContext.FirewallRules.Remove(rule);
}
