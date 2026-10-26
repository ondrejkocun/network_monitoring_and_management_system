using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Monitoring;

public interface IAgentlessDeviceChecker
{
    /// <summary>Checks every enabled device without an agent once and records the result.</summary>
    /// <returns>How long to wait before the next round: the global synchronization interval.</returns>
    Task<TimeSpan> CheckAllAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentlessDeviceChecker : IAgentlessDeviceChecker
{
    private const int MaxConcurrentProbes = 16;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly IDeviceRepository _deviceRepository;
    private readonly IMonitoringSettingsRepository _settingsRepository;
    private readonly IDeviceReachabilityProbe _probe;
    private readonly AvailabilityRecorder _recorder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public AgentlessDeviceChecker(
        IDeviceRepository deviceRepository,
        IMonitoringSettingsRepository settingsRepository,
        IDeviceReachabilityProbe probe,
        AvailabilityRecorder recorder,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _deviceRepository = deviceRepository;
        _settingsRepository = settingsRepository;
        _probe = probe;
        _recorder = recorder;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<TimeSpan> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _deviceRepository.GetEnabledAgentlessDevicesAsync(cancellationToken);

        if (devices.Count > 0)
        {
            // The network checks run in parallel; their results are recorded afterwards, one at a time.
            var results = (await ProbeAllAsync(devices, cancellationToken))
                .ToDictionary(probe => probe.Device.Id, probe => probe.Result);
            var now = _timeProvider.GetUtcNow();

            // When a device is changed meanwhile (for example disabled), only the recording starts again;
            // the network checks are not repeated.
            await ConcurrencyRetry.RunAsync(_unitOfWork, () => RecordAsync(results, now, cancellationToken));
        }

        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return TimeSpan.FromSeconds(settings.SyncIntervalSeconds);
    }

    /// <summary>
    /// Records the results on the devices as they are now. A device disabled or removed since it was checked is skipped.
    /// </summary>
    private async Task<int> RecordAsync(
        IReadOnlyDictionary<Guid, ReachabilityResult> results,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var devices = await _deviceRepository.GetEnabledAgentlessDevicesAsync(cancellationToken);
        var recorded = 0;

        foreach (var device in devices)
        {
            if (!results.TryGetValue(device.Id, out var result))
            {
                continue;
            }

            if (result.IsReachable)
            {
                await _recorder.RecordOnlineAsync(
                    device, now, result.ResponseTimeMs, reachedBy: result.ReachedBy, cancellationToken: cancellationToken);
            }
            else
            {
                _recorder.RecordUnreachable(device, now);
            }

            recorded++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return recorded;
    }

    private async Task<(Device Device, ReachabilityResult Result)[]> ProbeAllAsync(
        IReadOnlyList<Device> devices,
        CancellationToken cancellationToken)
    {
        using var limiter = new SemaphoreSlim(MaxConcurrentProbes);

        return await Task.WhenAll(devices.Select(async device =>
        {
            await limiter.WaitAsync(cancellationToken);

            try
            {
                // A device without an agent always has an IP address; the domain model guarantees it.
                return (device, await _probe.ProbeAsync(device.IpAddress!, device.TcpPort, ProbeTimeout, cancellationToken));
            }
            finally
            {
                limiter.Release();
            }
        }));
    }
}
