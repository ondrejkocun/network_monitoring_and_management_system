namespace NetworkMonitoringSystem.Application.Common;

/// <summary>
/// Saves everything changed through the repositories during one operation, as a single transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">Someone else changed a record this operation also changed.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets everything loaded and changed so far, so the operation can start again from fresh data.</summary>
    void DiscardChanges();
}

/// <summary>
/// A record changed between the moment it was read and the moment the changes were saved. Nothing was saved.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public static class ConcurrencyRetry
{
    public const int MaxAttempts = 3;

    /// <summary>
    /// Runs an operation that loads, changes and saves records, and runs it again from fresh data when someone else
    /// changed the same record meanwhile. The operation must load everything it changes itself.
    /// </summary>
    public static async Task<T> RunAsync<T>(IUnitOfWork unitOfWork, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                unitOfWork.DiscardChanges();
            }
        }
    }
}
