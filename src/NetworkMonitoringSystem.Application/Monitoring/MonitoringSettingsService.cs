using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

public sealed record MonitoringSettingsDto(
    int SyncIntervalSeconds,
    int OfflineAfterMissedSyncs,
    int MinSyncIntervalSeconds,
    int MaxSyncIntervalSeconds,
    int RetentionDays,
    int MaxOfflineAfterMissedSyncs,
    int MaxRetentionDays)
{
    public static MonitoringSettingsDto FromSettings(MonitoringSettings settings)
    {
        return new MonitoringSettingsDto(
            settings.SyncIntervalSeconds,
            settings.OfflineAfterMissedSyncs,
            MonitoringSettings.MinSyncIntervalSeconds,
            MonitoringSettings.MaxSyncIntervalSeconds,
            settings.RetentionDays,
            MonitoringSettings.MaxOfflineAfterMissedSyncs,
            MonitoringSettings.MaxRetentionDays);
    }
}

public interface IMonitoringSettingsService
{
    Task<MonitoringSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    /// <param name="userId">The user who made the change, recorded in the event.</param>
    /// <exception cref="ArgumentOutOfRangeException">The interval is outside the allowed limits.</exception>
    Task<MonitoringSettingsDto> ChangeSyncIntervalAsync(int seconds, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes how many synchronizations in a row a device may miss before it is considered offline.
    /// The next availability check already uses the new value.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The number is outside the allowed limits.</exception>
    Task<MonitoringSettingsDto> ChangeOfflineThresholdAsync(int missedSyncs, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>Changes how many days history is kept. The next clean-up already deletes by the new value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The number of days is outside the allowed limits.</exception>
    Task<MonitoringSettingsDto> ChangeRetentionAsync(int days, Guid? userId = null, CancellationToken cancellationToken = default);
}

public sealed class MonitoringSettingsService : IMonitoringSettingsService
{
    private readonly IMonitoringSettingsRepository _settingsRepository;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public MonitoringSettingsService(
        IMonitoringSettingsRepository settingsRepository,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _settingsRepository = settingsRepository;
        _history = history;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<MonitoringSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        return MonitoringSettingsDto.FromSettings(await _settingsRepository.GetAsync(cancellationToken));
    }

    public Task<MonitoringSettingsDto> ChangeSyncIntervalAsync(int seconds, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(
            settings => settings.SyncIntervalSeconds,
            (settings, value) => settings.ChangeSyncInterval(value),
            seconds,
            (previous, value) => $"Synchronization interval changed from {previous} s to {value} s.",
            userId,
            cancellationToken);
    }

    public Task<MonitoringSettingsDto> ChangeOfflineThresholdAsync(int missedSyncs, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(
            settings => settings.OfflineAfterMissedSyncs,
            (settings, value) => settings.ChangeOfflineThreshold(value),
            missedSyncs,
            (previous, value) => $"Devices are now considered offline after {value} missed synchronizations instead of {previous}.",
            userId,
            cancellationToken);
    }

    public Task<MonitoringSettingsDto> ChangeRetentionAsync(int days, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(
            settings => settings.RetentionDays,
            (settings, value) => settings.ChangeRetention(value),
            days,
            (previous, value) => $"History is now kept for {value} days instead of {previous}.",
            userId,
            cancellationToken);
    }

    /// <summary>Changes one setting and, when its value really changed, writes an event with the user who did it.</summary>
    private async Task<MonitoringSettingsDto> ChangeAsync(
        Func<MonitoringSettings, int> read,
        Action<MonitoringSettings, int> change,
        int value,
        Func<int, int, string> describe,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        var previous = read(settings);

        change(settings, value);

        if (previous != value)
        {
            _history.AddEvent(new MonitoringEvent(
                _timeProvider.GetUtcNow(),
                MonitoringEventType.SettingsChanged,
                EventSeverity.Info,
                describe(previous, value),
                userId: userId));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return MonitoringSettingsDto.FromSettings(settings);
    }
}
