using System.Runtime.InteropServices;
using Grpc.Core;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Agent.Metrics;
using NetworkMonitoringSystem.Agent.Traffic;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent;

/// <summary>
/// Performs one round of communication with the server: registers the device if needed and reports
/// that it is alive together with its measured system resources and, when asked for, a summary of its traffic.
/// </summary>
public sealed class AgentReporter
{
    private readonly AgentApi.AgentApiClient _client;
    private readonly IAgentIdentityStore _identityStore;
    private readonly ISystemMetricsCollector _metricsCollector;
    private readonly ISystemActivityCollector _activityCollector;
    private readonly ITrafficCapture _trafficCapture;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentReporter> _logger;

    public AgentReporter(
        AgentApi.AgentApiClient client,
        IAgentIdentityStore identityStore,
        ISystemMetricsCollector metricsCollector,
        ISystemActivityCollector activityCollector,
        ITrafficCapture trafficCapture,
        IOptions<AgentOptions> options,
        ILogger<AgentReporter> logger)
    {
        _client = client;
        _identityStore = identityStore;
        _metricsCollector = metricsCollector;
        _activityCollector = activityCollector;
        _trafficCapture = trafficCapture;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Returns how long to wait before the next round.</summary>
    public async Task<TimeSpan> ReportOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var identity = _identityStore.Load();

            if (identity is null)
            {
                return await RegisterAsync(cancellationToken);
            }

            var reply = await _client.HeartbeatAsync(
                new HeartbeatRequest
                {
                    Credentials = new AgentCredentials { DeviceId = identity.DeviceId, AgentKey = identity.AgentKey },
                    Metrics = CollectMetrics(),
                    Activity = CollectActivity(),
                    // Traffic counted while a report fails is lost with it; capture goes on for the next one.
                    Traffic = _trafficCapture.IsEnabled ? _trafficCapture.TakeSummary() : null,
                    IpAddress = NetworkAddress.FindPrimary(),
                },
                cancellationToken: cancellationToken);

            // The administrator switches capture on and off in the desktop app; the server passes it on here.
            _trafficCapture.SetEnabled(reply.CaptureTraffic);

            return TimeSpan.FromSeconds(reply.SyncIntervalSeconds);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated)
        {
            // The server no longer accepts the stored identity, so the next round registers again.
            _logger.LogWarning("Server rejected the stored agent identity; the agent will register again.");
            _identityStore.Clear();

            // Nothing captured could be delivered now; the server asks again once the agent is accepted.
            _trafficCapture.SetEnabled(false);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.PermissionDenied)
        {
            _logger.LogError(
                "Server refused the registration. Check the enrollment token and whether the device is enabled. "
                + "A device the server still sees as online can register again only after it is marked offline.");
        }
        catch (RpcException exception) when (exception.StatusCode != StatusCode.Cancelled)
        {
            _logger.LogWarning("Server is not reachable ({Status}): {Detail}", exception.StatusCode, exception.Status.Detail);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or RpcException))
        {
            // For example the identity cannot be written. The loop must go on, or the whole service would stop.
            _logger.LogError(exception, "Reporting to the server failed unexpectedly; it will be tried again.");
        }

        return RetryInterval;
    }

    private SystemMetrics? CollectMetrics()
    {
        try
        {
            return _metricsCollector.Collect();
        }
        catch (Exception exception)
        {
            // A failed measurement must not stop the device from reporting that it is alive.
            _logger.LogWarning(exception, "System resources could not be measured; reporting without them.");

            return null;
        }
    }

    private SystemActivity? CollectActivity()
    {
        try
        {
            return _activityCollector.Collect();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Processes, ports and connections could not be read; reporting without them.");

            return null;
        }
    }

    private TimeSpan RetryInterval => TimeSpan.FromSeconds(Math.Max(1, _options.RetryIntervalSeconds));

    private async Task<TimeSpan> RegisterAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.EnrollmentToken))
        {
            _logger.LogError("The agent is not registered and no enrollment token is configured ({Key}).", $"{AgentOptions.SectionName}:{nameof(AgentOptions.EnrollmentToken)}");

            return RetryInterval;
        }

        var reply = await _client.RegisterAsync(
            new RegisterRequest
            {
                EnrollmentToken = _options.EnrollmentToken,
                HostName = Environment.MachineName,
                OperatingSystem = RuntimeInformation.OSDescription,
                IpAddress = NetworkAddress.FindPrimary(),
            },
            cancellationToken: cancellationToken);

        _identityStore.Save(new AgentIdentity(reply.Credentials.DeviceId, reply.Credentials.AgentKey));
        _logger.LogInformation("Agent registered with the server as device {DeviceId}.", reply.Credentials.DeviceId);

        return TimeSpan.FromSeconds(reply.SyncIntervalSeconds);
    }
}
