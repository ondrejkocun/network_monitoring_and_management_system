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
    private readonly IEnrollmentCodeRepository _enrollmentCodes;
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
        IEnrollmentCodeRepository enrollmentCodes,
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
        _enrollmentCodes = enrollmentCodes;
        _timeProvider = timeProvider;
    }

    public Task<AgentRegistration> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ConcurrencyRetry.RunAsync(_unitOfWork, () => RegisterOnceAsync(request, cancellationToken));
    }

    private async Task<AgentRegistration> RegisterOnceAsync(RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.EnrollmentToken))
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

        // The shared token, when the server has one, admits any computer. Without it every computer needs its own code.
        var sharedToken = _enrollmentOptions.Value.EnrollmentToken;
        EnrollmentCode? code = null;

        if (string.IsNullOrEmpty(sharedToken) || !AgentKey.FixedTimeEquals(request.EnrollmentToken, sharedToken))
        {
            code = await _enrollmentCodes.GetByHashAsync(EnrollmentCodeText.Hash(request.EnrollmentToken), cancellationToken);

            if (code is null || code.StateAt(now) != EnrollmentCodeState.Valid)
            {
                throw new AgentAuthenticationException("Enrollment code is not valid, was already used, expired or was revoked.");
            }

            // A code made for a device registers only that device again; one made for a new computer cannot take
            // over a device the server already knows.
            if (code.DeviceId != device?.Id)
            {
                throw new AgentAuthenticationException("The enrollment code was made for another device.");
            }
        }

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
        device.RecordNetworkAddress(request.NetworkAddress);

        // Spent in the same save as the registration; two agents racing with one code conflict on its row.
        code?.Use(hostName, now);

        _history.AddEvent(new MonitoringEvent(
            now,
            MonitoringEventType.AgentRegistered,
            EventSeverity.Info,
            code is null
                ? $"Agent on device '{device.Name}' registered."
                : $"Agent on device '{device.Name}' registered with an enrollment code{EnrollmentCodeService.DescribeNote(code)}.",
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
        System.Net.IPAddress? networkAddress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        // The availability check may mark the same device offline at the same moment; the report then starts again
        // from the device as the check left it, so the device ends up online.
        return ConcurrencyRetry.RunAsync(
            _unitOfWork,
            () => RecordHeartbeatAsync(credentials, metrics, activity, traffic, networkAddress, cancellationToken));
    }

    private async Task<AgentHeartbeatResult> RecordHeartbeatAsync(
        AgentCredentials credentials,
        SystemMetricsReport? metrics,
        SystemActivityReport? activity,
        TrafficReport? traffic,
        System.Net.IPAddress? networkAddress,
        CancellationToken cancellationToken)
    {
        var device = await _deviceRepository.GetByIdAsync(credentials.DeviceId, cancellationToken);

        if (device is null || !AgentKey.IsValidFor(device, credentials))
        {
            throw new AgentAuthenticationException("Agent credentials are not valid.");
        }

        var now = _timeProvider.GetUtcNow();
        var settings = await _settingsRepository.GetAsync(cancellationToken);

        device.RecordNetworkAddress(networkAddress);

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
