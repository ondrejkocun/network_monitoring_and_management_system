namespace NetworkMonitoringSystem.Application.Common;

/// <summary>
/// Saves everything changed through the repositories during one operation, as a single transaction.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
