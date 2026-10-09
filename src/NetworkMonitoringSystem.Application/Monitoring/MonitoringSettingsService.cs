using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

public sealed record MonitoringSettingsDto(
    int SyncIntervalSeconds,
    int OfflineAfterMissedSyncs,
    int MinSyncIntervalSeconds,
    int MaxSyncIntervalSeconds)
{
    public static MonitoringSettingsDto FromSettings(MonitoringSettings settings)
    {
        return new MonitoringSettingsDto(
            settings.SyncIntervalSeconds,
            settings.OfflineAfterMissedSyncs,
            MonitoringSettings.MinSyncIntervalSeconds,
            MonitoringSettings.MaxSyncIntervalSeconds);
    }
}

public interface IMonitoringSettingsService
{
    Task<MonitoringSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentOutOfRangeException">The interval is outside the allowed limits.</exception>
    Task<MonitoringSettingsDto> ChangeSyncIntervalAsync(int seconds, CancellationToken cancellationToken = default);
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

    public async Task<MonitoringSettingsDto> ChangeSyncIntervalAsync(int seconds, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        var previous = settings.SyncIntervalSeconds;

        settings.ChangeSyncInterval(seconds);

        if (previous != seconds)
        {
            _history.AddEvent(new MonitoringEvent(
                _timeProvider.GetUtcNow(),
                MonitoringEventType.SettingsChanged,
                EventSeverity.Info,
                $"Synchronization interval changed from {previous} s to {seconds} s."));

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return MonitoringSettingsDto.FromSettings(settings);
    }
}
