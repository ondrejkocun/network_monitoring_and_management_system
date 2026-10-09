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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptions<AgentEnrollmentOptions> _enrollmentOptions;
    private readonly TimeProvider _timeProvider;

    public AgentService(
        IDeviceRepository deviceRepository,
        IMonitoringSettingsRepository settingsRepository,
        IMonitoringHistoryRepository history,
        AvailabilityRecorder recorder,
        IUnitOfWork unitOfWork,
        IOptions<AgentEnrollmentOptions> enrollmentOptions,
        TimeProvider timeProvider)
    {
        _deviceRepository = deviceRepository;
        _settingsRepository = settingsRepository;
        _history = history;
        _recorder = recorder;
        _unitOfWork = unitOfWork;
        _enrollmentOptions = enrollmentOptions;
        _timeProvider = timeProvider;
    }

    public async Task<AgentRegistration> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

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

    public async Task<AgentHeartbeatResult> ReportHeartbeatAsync(
        AgentCredentials credentials,
        SystemMetricsReport? metrics = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var device = await _deviceRepository.GetByIdAsync(credentials.DeviceId, cancellationToken);

        if (device is null
            || !device.IsEnabled
            || string.IsNullOrEmpty(credentials.AgentKey)
            || !AgentKey.Matches(credentials.AgentKey, device.AgentKeyHash))
        {
            throw new AgentAuthenticationException("Agent credentials are not valid.");
        }

        await _recorder.RecordOnlineAsync(
            device,
            _timeProvider.GetUtcNow(),
            resources: metrics?.ToResourceUsage(),
            cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return new AgentHeartbeatResult(settings.SyncIntervalSeconds);
    }
}
