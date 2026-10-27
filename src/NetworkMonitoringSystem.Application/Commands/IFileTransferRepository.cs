using NetworkMonitoringSystem.Domain.Commands;

namespace NetworkMonitoringSystem.Application.Commands;

/// <summary>
/// Storage for files sent to devices. Additions are saved through <see cref="Common.IUnitOfWork"/>;
/// deletions of contents happen immediately.
/// </summary>
public interface IFileTransferRepository
{
    void Add(FileTransfer transfer);

    void AddChunk(FileTransferChunk chunk);

    Task<FileTransfer?> GetAsync(Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>Returns one part of the contents, or null when it is not stored (any more).</summary>
    Task<byte[]?> GetChunkAsync(Guid commandId, int index, CancellationToken cancellationToken = default);

    /// <summary>Deletes the contents of the file; its name, size and hash stay.</summary>
    Task DeleteContentAsync(Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the contents of every file whose command has finished, or was requested before the given time and so
    /// can no longer be carried out.
    /// </summary>
    /// <returns>Number of deleted parts.</returns>
    Task<int> DeleteContentOfFinishedCommandsAsync(DateTimeOffset requestedBefore, CancellationToken cancellationToken = default);
}

/// <summary>What the agent needs to know before it downloads a file.</summary>
public sealed record FileTransferDto(Guid CommandId, string FileName, long Size, string Sha256, int ChunkCount)
{
    public static FileTransferDto FromTransfer(FileTransfer transfer)
    {
        return new FileTransferDto(transfer.CommandId, transfer.FileName, transfer.Size, transfer.Sha256, transfer.ChunkCount);
    }
}
