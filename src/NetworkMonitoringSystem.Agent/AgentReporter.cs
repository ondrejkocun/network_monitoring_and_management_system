using System.Runtime.InteropServices;
using Grpc.Core;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent;

/// <summary>
/// Performs one round of communication with the server: registers the device if needed and reports that it is alive.
/// </summary>
public sealed class AgentReporter
{
    private readonly AgentApi.AgentApiClient _client;
    private readonly IAgentIdentityStore _identityStore;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentReporter> _logger;

    public AgentReporter(
        AgentApi.AgentApiClient client,
        IAgentIdentityStore identityStore,
        IOptions<AgentOptions> options,
        ILogger<AgentReporter> logger)
    {
        _client = client;
        _identityStore = identityStore;
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
                },
                cancellationToken: cancellationToken);

            return TimeSpan.FromSeconds(reply.SyncIntervalSeconds);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated)
        {
            // The server no longer accepts the stored identity, so the next round registers again.
            _logger.LogWarning("Server rejected the stored agent identity; the agent will register again.");
            _identityStore.Clear();
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.PermissionDenied)
        {
            _logger.LogError("Server refused the registration. Check the enrollment token and whether the device is enabled.");
        }
        catch (RpcException exception) when (exception.StatusCode != StatusCode.Cancelled)
        {
            _logger.LogWarning("Server is not reachable ({Status}): {Detail}", exception.StatusCode, exception.Status.Detail);
        }

        return RetryInterval;
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
            },
            cancellationToken: cancellationToken);

        _identityStore.Save(new AgentIdentity(reply.Credentials.DeviceId, reply.Credentials.AgentKey));
        _logger.LogInformation("Agent registered with the server as device {DeviceId}.", reply.Credentials.DeviceId);

        return TimeSpan.FromSeconds(reply.SyncIntervalSeconds);
    }
}
