using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Commands;
using NetworkMonitoringSystem.Domain.Commands;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Commands;

public sealed class EfRemoteCommandRepository : IRemoteCommandRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfRemoteCommandRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public void Add(RemoteCommand command) => _dbContext.RemoteCommands.Add(command);

    public Task<RemoteCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.RemoteCommands.FirstOrDefaultAsync(command => command.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteCommand>> GetUnfinishedAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RemoteCommands
            .Where(command => command.DeviceId == deviceId
                && (command.Status == RemoteCommandStatus.Pending || command.Status == RemoteCommandStatus.Sent))
            .OrderBy(command => command.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteCommand>> GetRecentAsync(Guid deviceId, int limit, CancellationToken cancellationToken = default)
    {
        return await _dbContext.RemoteCommands
            .AsNoTracking()
            .Where(command => command.DeviceId == deviceId)
            .OrderByDescending(command => command.RequestedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
