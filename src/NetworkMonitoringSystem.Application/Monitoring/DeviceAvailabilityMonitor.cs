using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;

namespace NetworkMonitoringSystem.Application.Monitoring;

public interface IDeviceAvailabilityMonitor
{
    /// <summary>
    /// Marks devices that have not been seen for too long as offline: agents that stopped reporting
    /// and devices without an agent that stopped answering the server's checks.
    /// </summary>
    /// <returns>Number of devices that went offline.</returns>
    Task<int> EvaluateAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns how long a device may go unseen before it is considered offline.</summary>
    Task<TimeSpan> GetOfflineThresholdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns how long to wait after the server starts before the first evaluation. While the server was down
    /// nobody could report, so devices get time to be seen again. One synchronization interval is enough:
    /// a running agent reports at least that often, and devices without an agent are checked right at start.
    /// </summary>
    Task<TimeSpan> GetStartupGracePeriodAsync(CancellationToken cancellationToken = default);
}

public sealed class DeviceAvailabilityMonitor : IDeviceAvailabilityMonitor
{
    /// <summary>Allows for the time an agent needs to notice the server is back and deliver its report.</summary>
    private static readonly TimeSpan StartupGraceMargin = TimeSpan.FromSeconds(20);

    private readonly IDeviceRepository _deviceRepository;
    private readonly IMonitoringSettingsRepository _settingsRepository;
    private readonly AvailabilityRecorder _recorder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DeviceAvailabilityMonitor(
        IDeviceRepository deviceRepository,
        IMonitoringSettingsRepository settingsRepository,
        AvailabilityRecorder recorder,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _deviceRepository = deviceRepository;
        _settingsRepository = settingsRepository;
        _recorder = recorder;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<int> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var threshold = await GetOfflineThresholdAsync(cancellationToken);

        var silentDevices = await _deviceRepository.GetOnlineDevicesNotSeenSinceAsync(now - threshold, cancellationToken);

        foreach (var device in silentDevices)
        {
            _recorder.RecordOffline(device, now);
        }

        if (silentDevices.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return silentDevices.Count;
    }

    public async Task<TimeSpan> GetStartupGracePeriodAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return TimeSpan.FromSeconds(settings.SyncIntervalSeconds) + StartupGraceMargin;
    }

    public async Task<TimeSpan> GetOfflineThresholdAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return TimeSpan.FromSeconds((long)settings.SyncIntervalSeconds * settings.OfflineAfterMissedSyncs);
    }
}
