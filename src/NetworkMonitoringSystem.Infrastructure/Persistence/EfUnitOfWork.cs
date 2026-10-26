using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Common;

namespace NetworkMonitoringSystem.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly MonitoringDbContext _dbContext;

    public EfUnitOfWork(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("A record was changed by someone else while it was being updated.", exception);
        }
    }

    public void DiscardChanges()
    {
        _dbContext.ChangeTracker.Clear();
    }
}
