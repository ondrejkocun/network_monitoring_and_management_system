using Grpc.Core;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Commands;
using NetworkMonitoringSystem.Contracts.Agents;
using AgentCredentials = NetworkMonitoringSystem.Application.Agents.AgentCredentials;
using DomainTransportProtocol = NetworkMonitoringSystem.Domain.Monitoring.TransportProtocol;
using DomainTrafficTransport = NetworkMonitoringSystem.Domain.Monitoring.TrafficTransport;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// gRPC endpoint for agents. Translates between the wire contract and the application layer.
/// </summary>
public sealed class AgentApiService : AgentApi.AgentApiBase
{
    private const int CommandPollIntervalSeconds = 5;

    private readonly IAgentService _agentService;
    private readonly IRemoteCommandService _commandService;
    private readonly ILogger<AgentApiService> _logger;

    public AgentApiService(IAgentService agentService, IRemoteCommandService commandService, ILogger<AgentApiService> logger)
    {
        _commandService = commandService;
        _agentService = agentService;
        _logger = logger;
    }

    public override async Task<RegisterReply> Register(RegisterRequest request, ServerCallContext context)
    {
        try
        {
            var registration = await _agentService.RegisterAsync(
                new RegisterAgentRequest(request.EnrollmentToken, request.HostName, request.OperatingSystem, NetworkAddressOf(request.IpAddress, context)),
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
                ToReport(request.Traffic),
                NetworkAddressOf(request.IpAddress, context),
                context.CancellationToken);

            return new HeartbeatReply { SyncIntervalSeconds = result.SyncIntervalSeconds, CaptureTraffic = result.CaptureTraffic };
        }
        catch (AgentAuthenticationException)
        {
            _logger.LogWarning("Heartbeat with invalid credentials for device {DeviceId} from {Peer}.", deviceId, context.Peer);

            throw Unauthenticated();
        }
    }

    public override async Task<FetchCommandsReply> FetchCommands(FetchCommandsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Credentials?.DeviceId, out var deviceId))
        {
            throw Unauthenticated();
        }

        try
        {
            var commands = await _commandService.FetchPendingAsync(
                new AgentCredentials(deviceId, request.Credentials.AgentKey),
                context.CancellationToken);

            var reply = new FetchCommandsReply { PollIntervalSeconds = CommandPollIntervalSeconds };
            reply.Commands.AddRange(commands.Select(command => new Contracts.Agents.AgentCommand
            {
                Id = command.Id.ToString(),
                // The two lists have the same numbers on purpose.
                Type = (CommandType)(int)command.Type,
                Parameter = command.Parameter ?? string.Empty,
            }));

            foreach (var command in commands)
            {
                _logger.LogInformation("Command {Type} {CommandId} handed to device {DeviceId}.", command.Type, command.Id, deviceId);
            }

            return reply;
        }
        catch (AgentAuthenticationException)
        {
            throw Unauthenticated();
        }
    }

    public override async Task<CommandResultReply> ReportCommandResult(CommandResultRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Credentials?.DeviceId, out var deviceId))
        {
            throw Unauthenticated();
        }

        try
        {
            var accepted = Guid.TryParse(request.CommandId, out var commandId)
                && await _commandService.CompleteAsync(
                    new AgentCredentials(deviceId, request.Credentials.AgentKey),
                    commandId,
                    request.Succeeded,
                    request.Message,
                    context.CancellationToken);

            if (!accepted)
            {
                _logger.LogWarning("Device {DeviceId} reported a result for a command it was not handed.", deviceId);

                throw new RpcException(new Status(StatusCode.NotFound, "No such command is waiting for a result."));
            }

            return new CommandResultReply();
        }
        catch (AgentAuthenticationException)
        {
            throw Unauthenticated();
        }
    }

    public override async Task DownloadFile(
        DownloadFileRequest request,
        IServerStreamWriter<DownloadFileReply> responseStream,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.Credentials?.DeviceId, out var deviceId))
        {
            throw Unauthenticated();
        }

        FileTransferDto? file;

        try
        {
            file = Guid.TryParse(request.CommandId, out var commandId)
                ? await _commandService.GetFileForAgentAsync(new AgentCredentials(deviceId, request.Credentials.AgentKey), commandId, context.CancellationToken)
                : null;
        }
        catch (AgentAuthenticationException)
        {
            throw Unauthenticated();
        }

        if (file is null)
        {
            _logger.LogWarning("Device {DeviceId} asked for a file of a command it is not carrying out.", deviceId);

            throw new RpcException(new Status(StatusCode.NotFound, "No such file is waiting for this device."));
        }

        await responseStream.WriteAsync(new DownloadFileReply
        {
            Header = new FileHeader { FileName = file.FileName, Size = file.Size, Sha256 = file.Sha256 },
        });

        // One part at a time, so the server never holds the whole file for a download.
        for (var index = 0; index < file.ChunkCount; index++)
        {
            var data = await _commandService.ReadFileChunkAsync(file.CommandId, index, context.CancellationToken)
                ?? throw new RpcException(new Status(StatusCode.NotFound, "The contents of the file are no longer stored."));

            await responseStream.WriteAsync(new DownloadFileReply { Data = Google.Protobuf.ByteString.CopyFrom(data) });
        }

        _logger.LogInformation("File {FileName} ({Size} bytes) sent to device {DeviceId}.", file.FileName, file.Size, deviceId);
    }

    /// <summary>
    /// The address the agent reported, or else the one its request came from. The latter is what the server sees, which
    /// behind a router may be the router's; loopback is ignored later, so an agent on the server's own computer gets none.
    /// </summary>
    private static System.Net.IPAddress? NetworkAddressOf(string reported, ServerCallContext context)
    {
        if (System.Net.IPAddress.TryParse(reported, out var address))
        {
            return address;
        }

        var remote = context.GetHttpContext().Connection.RemoteIpAddress;

        return remote is { IsIPv4MappedToIPv6: true } ? remote.MapToIPv4() : remote;
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

    private static TrafficReport? ToReport(TrafficSummary? traffic)
    {
        if (traffic is null)
        {
            return null;
        }

        var periodEnd = traffic.PeriodEnd?.ToDateTimeOffset() ?? DateTimeOffset.UnixEpoch;

        return new TrafficReport(
            traffic.PeriodStart?.ToDateTimeOffset() ?? periodEnd,
            periodEnd,
            traffic.Conversations
                .Where(conversation => conversation.Transport != TrafficTransport.Unspecified)
                .Select(conversation => new ConversationReport(
                    conversation.Transport switch
                    {
                        TrafficTransport.Tcp => DomainTrafficTransport.Tcp,
                        TrafficTransport.Udp => DomainTrafficTransport.Udp,
                        TrafficTransport.Icmp => DomainTrafficTransport.Icmp,
                        _ => DomainTrafficTransport.Other,
                    },
                    conversation.Protocol,
                    conversation.RemoteAddress,
                    conversation.HasServicePort ? ToInt32(conversation.ServicePort) : null,
                    ToInt64(conversation.PacketsSent),
                    ToInt64(conversation.PacketsReceived),
                    ToInt64(conversation.BytesSent),
                    ToInt64(conversation.BytesReceived)))
                .ToList(),
            string.IsNullOrWhiteSpace(traffic.CaptureProblem) ? null : traffic.CaptureProblem);
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
