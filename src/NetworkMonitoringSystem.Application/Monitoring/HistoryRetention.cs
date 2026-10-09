namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>How many history records one clean-up removed.</summary>
public readonly record struct HistoryCleanupResult(int Snapshots, int Outages, int Events)
{
    public int Total => Snapshots + Outages + Events;
}

public interface IHistoryRetentionService
{
    /// <summary>
    /// Deletes history older than the configured retention period: snapshots, outages that have ended
    /// and events. An outage that is still in progress is kept however old it is.
    /// </summary>
    Task<HistoryCleanupResult> CleanUpAsync(CancellationToken cancellationToken = default);
}

public sealed class HistoryRetentionService : IHistoryRetentionService
{
    private readonly IMonitoringSettingsRepository _settingsRepository;
    private readonly IMonitoringHistoryRepository _history;
    private readonly TimeProvider _timeProvider;

    public HistoryRetentionService(
        IMonitoringSettingsRepository settingsRepository,
        IMonitoringHistoryRepository history,
        TimeProvider timeProvider)
    {
        _settingsRepository = settingsRepository;
        _history = history;
        _timeProvider = timeProvider;
    }

    public async Task<HistoryCleanupResult> CleanUpAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        var cutoff = _timeProvider.GetUtcNow().AddDays(-settings.RetentionDays);

        return await _history.DeleteOlderThanAsync(cutoff, cancellationToken);
    }
}
