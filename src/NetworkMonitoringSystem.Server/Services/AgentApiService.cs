using Grpc.Core;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Contracts.Agents;
using AgentCredentials = NetworkMonitoringSystem.Application.Agents.AgentCredentials;
using DomainTransportProtocol = NetworkMonitoringSystem.Domain.Monitoring.TransportProtocol;

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
                ToReport(request.Metrics),
                ToReport(request.Activity),
                context.CancellationToken);

            return new HeartbeatReply { SyncIntervalSeconds = result.SyncIntervalSeconds };
        }
        catch (AgentAuthenticationException)
        {
            _logger.LogWarning("Heartbeat with invalid credentials for device {DeviceId} from {Peer}.", deviceId, context.Peer);

            throw Unauthenticated();
        }
    }

    private static SystemMetricsReport? ToReport(SystemMetrics? metrics)
    {
        if (metrics is null)
        {
            return null;
        }

        return new SystemMetricsReport(
            metrics.HasCpuUsagePercent ? metrics.CpuUsagePercent : null,
            ToInt64(metrics.MemoryTotalBytes),
            ToInt64(metrics.MemoryUsedBytes),
            metrics.Disks.Select(disk => new DiskReport(disk.Name, ToInt64(disk.TotalBytes), ToInt64(disk.FreeBytes))).ToList(),
            metrics.NetworkInterfaces
                .Select(networkInterface => new NetworkInterfaceReport(
                    networkInterface.Name,
                    networkInterface.MacAddress,
                    networkInterface.IpAddress,
                    networkInterface.IsUp,
                    ToInt64(networkInterface.BytesSent),
                    ToInt64(networkInterface.BytesReceived)))
                .ToList());
    }

    private static SystemActivityReport? ToReport(SystemActivity? activity)
    {
        if (activity is null)
        {
            return null;
        }

        return new SystemActivityReport(
            activity.Processes
                .Select(process => new ProcessReport(
                    ToInt32(process.Pid),
                    process.Name,
                    process.StartedAt?.ToDateTimeOffset(),
                    process.HasCpuUsagePercent ? process.CpuUsagePercent : null,
                    ToInt64(process.MemoryBytes)))
                .ToList(),
            activity.ListeningPorts
                .Where(port => port.Protocol != TransportProtocol.Unspecified)
                .Select(port => new ListeningPortReport(ToDomain(port.Protocol), port.LocalAddress, ToInt32(port.Port), ToInt32(port.Pid)))
                .ToList(),
            activity.Connections
                .Where(connection => connection.Protocol != TransportProtocol.Unspecified)
                .Select(connection => new ConnectionReport(
                    ToDomain(connection.Protocol),
                    connection.LocalAddress,
                    ToInt32(connection.LocalPort),
                    connection.RemoteAddress,
                    ToInt32(connection.RemotePort),
                    connection.State,
                    ToInt32(connection.Pid)))
                .ToList());
    }

    private static DomainTransportProtocol ToDomain(TransportProtocol protocol)
    {
        return protocol == TransportProtocol.Udp ? DomainTransportProtocol.Udp : DomainTransportProtocol.Tcp;
    }

    private static int ToInt32(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    private static long ToInt64(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    private static RpcException Unauthenticated()
    {
        return new RpcException(new Status(StatusCode.Unauthenticated, "Agent credentials are not valid."));
    }
}
