using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Agents;

public sealed class AgentService : IAgentService
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IMonitoringSettingsRepository _settingsRepository;
    private readonly IMonitoringHistoryRepository _history;
    private readonly AvailabilityRecorder _recorder;
    private readonly DeviceActivityRecorder _activityRecorder;
    private readonly IDeviceActivityRepository _activity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptions<AgentEnrollmentOptions> _enrollmentOptions;
    private readonly TimeProvider _timeProvider;

    public AgentService(
        IDeviceRepository deviceRepository,
        IMonitoringSettingsRepository settingsRepository,
        IMonitoringHistoryRepository history,
        AvailabilityRecorder recorder,
        DeviceActivityRecorder activityRecorder,
        IDeviceActivityRepository activity,
        IUnitOfWork unitOfWork,
        IOptions<AgentEnrollmentOptions> enrollmentOptions,
        TimeProvider timeProvider)
    {
        _deviceRepository = deviceRepository;
        _settingsRepository = settingsRepository;
        _history = history;
        _recorder = recorder;
        _activityRecorder = activityRecorder;
        _activity = activity;
        _unitOfWork = unitOfWork;
        _enrollmentOptions = enrollmentOptions;
        _timeProvider = timeProvider;
    }

    public Task<AgentRegistration> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ConcurrencyRetry.RunAsync(_unitOfWork, () => RegisterOnceAsync(request, cancellationToken));
    }

    private async Task<AgentRegistration> RegisterOnceAsync(RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        var expectedToken = _enrollmentOptions.Value.EnrollmentToken;

        if (string.IsNullOrEmpty(expectedToken)
            || string.IsNullOrEmpty(request.EnrollmentToken)
            || !AgentKey.FixedTimeEquals(request.EnrollmentToken, expectedToken))
        {
            throw new AgentAuthenticationException("Enrollment token is not valid.");
        }

        if (string.IsNullOrWhiteSpace(request.HostName))
        {
            throw new ArgumentException("Host name must not be empty.", nameof(request));
        }

        var hostName = request.HostName.Trim();
        var now = _timeProvider.GetUtcNow();
        var agentKey = AgentKey.Generate();

        // An agent that lost its identity (for example after a reinstall) takes over its existing device
        // instead of creating a duplicate.
        var device = await _deviceRepository.GetAgentDeviceByHostNameAsync(hostName, cancellationToken);

        if (device is { IsEnabled: false })
        {
            throw new AgentAuthenticationException("Device is disabled.");
        }

        // A device whose agent is reporting cannot be taken over, so knowing the enrollment token
        // is not enough to push a working agent out.
        if (device is { Status: DeviceStatus.Online })
        {
            throw new AgentAuthenticationException("Device is online and already has an agent.");
        }

        if (device is null)
        {
            device = new Device(Guid.NewGuid(), hostName, MonitoringMode.Agent, hostName, ipAddress: null, now);
            _deviceRepository.Add(device);
        }

        device.AssignAgentIdentity(AgentKey.Hash(agentKey), request.OperatingSystem);

        _history.AddEvent(new MonitoringEvent(
            now,
            MonitoringEventType.AgentRegistered,
            EventSeverity.Info,
            $"Agent on device '{device.Name}' registered.",
            device.Id));
        await _recorder.RecordOnlineAsync(device, now, cancellationToken: cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return new AgentRegistration(new AgentCredentials(device.Id, agentKey), settings.SyncIntervalSeconds);
    }

    public Task<AgentHeartbeatResult> ReportHeartbeatAsync(
        AgentCredentials credentials,
        SystemMetricsReport? metrics = null,
        SystemActivityReport? activity = null,
        TrafficReport? traffic = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        // The availability check may mark the same device offline at the same moment; the report then starts again
        // from the device as the check left it, so the device ends up online.
        return ConcurrencyRetry.RunAsync(
            _unitOfWork,
            () => RecordHeartbeatAsync(credentials, metrics, activity, traffic, cancellationToken));
    }

    private async Task<AgentHeartbeatResult> RecordHeartbeatAsync(
        AgentCredentials credentials,
        SystemMetricsReport? metrics,
        SystemActivityReport? activity,
        TrafficReport? traffic,
        CancellationToken cancellationToken)
    {
        var device = await _deviceRepository.GetByIdAsync(credentials.DeviceId, cancellationToken);

        if (device is null || !AgentKey.IsValidFor(device, credentials))
        {
            throw new AgentAuthenticationException("Agent credentials are not valid.");
        }

        var now = _timeProvider.GetUtcNow();
        var settings = await _settingsRepository.GetAsync(cancellationToken);

        var snapshot = await _recorder.RecordOnlineAsync(
            device,
            now,
            resources: metrics?.ToResourceUsage(),
            cancellationToken: cancellationToken);

        if (activity is not null)
        {
            await _activityRecorder.RecordAsync(device, activity, snapshot, settings.TopProcessCount, now, cancellationToken);
        }

        // Traffic is stored only while capture is switched on for the device. A summary that arrives after it was
        // switched off is dropped: the administrator no longer wants this device's communication recorded.
        if (traffic is not null && device.CaptureTraffic)
        {
            device.RecordTrafficCaptureProblem(traffic.CaptureProblem);
            _activity.AddTrafficRecords(traffic.ToRecords(device.Id, now));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AgentHeartbeatResult(settings.SyncIntervalSeconds, device.CaptureTraffic);
    }
}
