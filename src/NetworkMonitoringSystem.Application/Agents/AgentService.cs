using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Agents;

public sealed class AgentService : IAgentService
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IMonitoringSettingsRepository _settingsRepository;
    private readonly IOptions<AgentEnrollmentOptions> _enrollmentOptions;
    private readonly TimeProvider _timeProvider;

    public AgentService(
        IDeviceRepository deviceRepository,
        IMonitoringSettingsRepository settingsRepository,
        IOptions<AgentEnrollmentOptions> enrollmentOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(deviceRepository);
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(enrollmentOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _deviceRepository = deviceRepository;
        _settingsRepository = settingsRepository;
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

        var isNew = device is null;
        device ??= new Device(Guid.NewGuid(), hostName, MonitoringMode.Agent, hostName, ipAddress: null, now);

        device.AssignAgentIdentity(AgentKey.Hash(agentKey), request.OperatingSystem);
        device.RecordContact(now);

        if (isNew)
        {
            await _deviceRepository.AddAsync(device, cancellationToken);
        }
        else
        {
            await _deviceRepository.UpdateAsync(device, cancellationToken);
        }

        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return new AgentRegistration(new AgentCredentials(device.Id, agentKey), settings.SyncIntervalSeconds);
    }

    public async Task<AgentHeartbeatResult> ReportHeartbeatAsync(AgentCredentials credentials, CancellationToken cancellationToken = default)
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

        device.RecordContact(_timeProvider.GetUtcNow());
        await _deviceRepository.UpdateAsync(device, cancellationToken);

        var settings = await _settingsRepository.GetAsync(cancellationToken);

        return new AgentHeartbeatResult(settings.SyncIntervalSeconds);
    }
}
