using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Commands;
using NetworkMonitoringSystem.Domain.Commands;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Commands;

public sealed class EfFileTransferRepository : IFileTransferRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfFileTransferRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public void Add(FileTransfer transfer) => _dbContext.FileTransfers.Add(transfer);

    public void AddChunk(FileTransferChunk chunk) => _dbContext.FileTransferChunks.Add(chunk);

    public Task<FileTransfer?> GetAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        return _dbContext.FileTransfers.AsNoTracking().FirstOrDefaultAsync(transfer => transfer.CommandId == commandId, cancellationToken);
    }

    public async Task<byte[]?> GetChunkAsync(Guid commandId, int index, CancellationToken cancellationToken = default)
    {
        // Only the one part is read, so a large file never has to be in memory at once.
        return await _dbContext.FileTransferChunks
            .Where(chunk => chunk.CommandId == commandId && chunk.Index == index)
            .Select(chunk => chunk.Data)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task DeleteContentAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        return _dbContext.FileTransferChunks
            .Where(chunk => chunk.CommandId == commandId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> DeleteContentOfFinishedCommandsAsync(DateTimeOffset requestedBefore, CancellationToken cancellationToken = default)
    {
        return _dbContext.FileTransferChunks
            .Where(chunk => _dbContext.RemoteCommands.Any(command =>
                command.Id == chunk.CommandId
                && (command.Status == RemoteCommandStatus.Succeeded
                    || command.Status == RemoteCommandStatus.Failed
                    || command.Status == RemoteCommandStatus.Expired
                    || command.RequestedAt < requestedBefore)))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
