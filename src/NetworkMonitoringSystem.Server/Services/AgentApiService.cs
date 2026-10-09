using Grpc.Core;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Contracts.Agents;
using AgentCredentials = NetworkMonitoringSystem.Application.Agents.AgentCredentials;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// gRPC endpoint for agents. Translates between the wire contract and the application layer.
/// </summary>
public sealed class AgentApiService : AgentApi.AgentApiBase
{
    private readonly IAgentService _agentService;
    private readonly ILogger<AgentApiService> _logger;

    public AgentApiService(IAgentService agentService, ILogger<AgentApiService> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    public override async Task<RegisterReply> Register(RegisterRequest request, ServerCallContext context)
    {
        try
        {
            var registration = await _agentService.RegisterAsync(
                new RegisterAgentRequest(request.EnrollmentToken, request.HostName, request.OperatingSystem),
                context.CancellationToken);

            _logger.LogInformation(
                "Agent on host {HostName} registered as device {DeviceId}.",
                request.HostName,
                registration.Credentials.DeviceId);

            return new RegisterReply
            {
                Credentials = new Contracts.Agents.AgentCredentials
                {
                    DeviceId = registration.Credentials.DeviceId.ToString(),
                    AgentKey = registration.Credentials.AgentKey,
                },
                SyncIntervalSeconds = registration.SyncIntervalSeconds,
            };
        }
        catch (AgentAuthenticationException)
        {
            _logger.LogWarning("Registration of an agent on host {HostName} from {Peer} was refused.", request.HostName, context.Peer);

            throw new RpcException(new Status(StatusCode.PermissionDenied, "Registration was refused."));
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    public override async Task<HeartbeatReply> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Credentials?.DeviceId, out var deviceId))
        {
            throw Unauthenticated();
        }

        try
        {
            var result = await _agentService.ReportHeartbeatAsync(
                new AgentCredentials(deviceId, request.Credentials.AgentKey),
                context.CancellationToken);

            return new HeartbeatReply { SyncIntervalSeconds = result.SyncIntervalSeconds };
        }
        catch (AgentAuthenticationException)
        {
            _logger.LogWarning("Heartbeat with invalid credentials for device {DeviceId} from {Peer}.", deviceId, context.Peer);

            throw Unauthenticated();
        }
    }

    private static RpcException Unauthenticated()
    {
        return new RpcException(new Status(StatusCode.Unauthenticated, "Agent credentials are not valid."));
    }
}
