using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;

namespace NetworkMonitoringSystem.Application.Monitoring;

public interface IDeviceAvailabilityMonitor
{
    /// <summary>Marks devices whose agents stopped reporting as offline.</summary>
    /// <returns>Number of devices that went offline.</returns>
    Task<int> EvaluateAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns how long an agent may stay silent before its device is considered offline.</summary>
    Task<TimeSpan> GetOfflineThresholdAsync(CancellationToken cancellationToken = default);
}

public sealed class DeviceAvailabilityMonitor : IDeviceAvailabilityMonitor
{
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

        var silentDevices = await _deviceRepository.GetOnlineAgentDevicesNotSeenSinceAsync(now - threshold, cancellationToken);

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

    public async Task<TimeSpan> GetOfflineThresholdAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return TimeSpan.FromSeconds((long)settings.SyncIntervalSeconds * settings.OfflineAfterMissedSyncs);
    }
}
