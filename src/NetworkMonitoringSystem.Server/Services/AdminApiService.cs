using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Contracts.Admin;
using DomainEventSeverity = NetworkMonitoringSystem.Domain.Monitoring.EventSeverity;
using DomainDeviceStatus =NetworkMonitoringSystem.Domain.Devices.DeviceStatus;
using DomainMonitoringMode = NetworkMonitoringSystem.Domain.Devices.MonitoringMode;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// gRPC endpoint for the administrator's desktop application. Translates between the wire contract
/// and the application layer.
/// </summary>
public sealed class AdminApiService : AdminApi.AdminApiBase
{
    private readonly IDeviceService _deviceService;
    private readonly IMonitoringSettingsService _settingsService;
    private readonly IDeviceHistoryService _historyService;
    private readonly IAuthenticationService _authenticationService;
    private readonly DeniedAccessLog _deniedAccessLog;

    public AdminApiService(
        IDeviceService deviceService,
        IMonitoringSettingsService settingsService,
        IDeviceHistoryService historyService,
        IAuthenticationService authenticationService,
        DeniedAccessLog deniedAccessLog)
    {
        _historyService = historyService;
        _authenticationService = authenticationService;
        _deniedAccessLog = deniedAccessLog;
        _deviceService = deviceService;
        _settingsService = settingsService;
    }

    public override async Task<LoginReply> Login(LoginRequest request, ServerCallContext context)
    {
        try
        {
            var result = await _authenticationService.LoginAsync(request.UserName, request.Password, context.CancellationToken);

            var reply = new LoginReply
            {
                Token = result.Token,
                ExpiresAt = Timestamp.FromDateTimeOffset(result.ExpiresAt),
                UserName = result.User.UserName,
            };
            reply.Permissions.AddRange(System.Enum.GetValues<Permission>().Where(result.User.Can).Select(permission => permission.ToString()));

            return reply;
        }
        catch (AuthenticationFailedException exception)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, exception.Message));
        }
    }

    public override async Task<LogoutReply> Logout(LogoutRequest request, ServerCallContext context)
    {
        await _authenticationService.LogoutAsync(AdminAuthenticationInterceptor.ReadToken(context) ?? string.Empty, context.CancellationToken);

        return new LogoutReply();
    }

    public override async Task<ListDevicesReply> ListDevices(ListDevicesRequest request, ServerCallContext context)
    {
        var user = AdminAuthenticationInterceptor.GetUser(context);

        if (!user.CanOnSomeDevice(Permission.ViewDevices))
        {
            throw await DenyAsync(context, user, deviceId: null);
        }

        var devices = await _deviceService.GetDevicesAsync(context.CancellationToken);

        // A user whose rules name single devices sees only those.
        var reply = new ListDevicesReply();
        reply.Devices.AddRange(devices.Where(device => user.CanOnDevice(Permission.ViewDevices, device.Id)).Select(ToDeviceInfo));

        return reply;
    }

    public override async Task<DeviceInfo> AddAgentlessDevice(AddAgentlessDeviceRequest request, ServerCallContext context)
    {
        await RequireAsync(context, Permission.ManageDevices);

        try
        {
            var device = await _deviceService.RegisterDeviceAsync(
                new RegisterDeviceRequest(request.Name, DomainMonitoringMode.Agentless, IpAddress: request.IpAddress),
                context.CancellationToken);

            return ToDeviceInfo(device);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    public override async Task<RemoveDeviceReply> RemoveDevice(RemoveDeviceRequest request, ServerCallContext context)
    {
        var id = await AuthorizeForDeviceAsync(context, Permission.ManageDevices, request.Id);

        if (id is null || !await _deviceService.RemoveDeviceAsync(id.Value, context.CancellationToken))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."));
        }

        return new RemoveDeviceReply();
    }

    public override async Task<DeviceResourcesReply> GetDeviceResources(GetDeviceResourcesRequest request, ServerCallContext context)
    {
        var resources = await AuthorizeForDeviceAsync(context, Permission.ViewDevices, request.Id) is { } id
            ? await _deviceService.GetLatestResourcesAsync(id, context.CancellationToken)
            : null;

        if (resources is null)
        {
            return new DeviceResourcesReply { HasData = false };
        }

        var reply = new DeviceResourcesReply
        {
            HasData = true,
            RecordedAt = Timestamp.FromDateTimeOffset(resources.RecordedAt),
            MemoryTotalBytes = (ulong)resources.MemoryTotalBytes,
            MemoryUsedBytes = (ulong)resources.MemoryUsedBytes,
        };

        if (resources.CpuUsagePercent is { } cpuUsagePercent)
        {
            reply.CpuUsagePercent = cpuUsagePercent;
        }

        reply.Disks.AddRange(resources.Disks.Select(disk => new DiskInfo
        {
            Name = disk.Name,
            TotalBytes = (ulong)disk.TotalBytes,
            FreeBytes = (ulong)disk.FreeBytes,
        }));
        reply.NetworkInterfaces.AddRange(resources.NetworkInterfaces.Select(networkInterface => new NetworkInterfaceInfo
        {
            Name = networkInterface.Name,
            MacAddress = networkInterface.MacAddress ?? string.Empty,
            IpAddress = networkInterface.IpAddress ?? string.Empty,
            IsUp = networkInterface.IsUp,
            BytesSent = (ulong)networkInterface.BytesSent,
            BytesReceived = (ulong)networkInterface.BytesReceived,
        }));

        return reply;
    }

    public override async Task<DeviceActivityReply> GetDeviceActivity(GetDeviceActivityRequest request, ServerCallContext context)
    {
        var reply = new DeviceActivityReply();

        if (await AuthorizeForDeviceAsync(context, Permission.ViewDevices, request.Id) is not { } id)
        {
            return reply;
        }

        var activity = await _deviceService.GetActivityAsync(id, context.CancellationToken);

        reply.Processes.AddRange(activity.Processes.Select(process =>
        {
            var detail = new ProcessDetail
            {
                Pid = (uint)process.Pid,
                Name = process.Name,
                StartedAt = Timestamp.FromDateTimeOffset(process.StartedAt),
                HasUsage = process.HasUsage,
                MemoryBytes = (ulong)process.MemoryBytes,
            };

            if (process.CpuUsagePercent is { } cpuUsagePercent)
            {
                detail.CpuUsagePercent = cpuUsagePercent;
            }

            return detail;
        }));
        reply.ListeningPorts.AddRange(activity.ListeningPorts.Select(port => new ListeningPortDetail
        {
            Protocol = port.Protocol.ToString().ToUpperInvariant(),
            LocalAddress = port.LocalAddress,
            Port = (uint)port.Port,
            OpenedAt = Timestamp.FromDateTimeOffset(port.OpenedAt),
            Pid = (uint)(port.Pid ?? 0),
            ProcessName = port.ProcessName ?? string.Empty,
        }));
        reply.Connections.AddRange(activity.Connections.Select(connection => new ConnectionDetail
        {
            Protocol = connection.Protocol.ToString().ToUpperInvariant(),
            LocalAddress = connection.LocalAddress,
            LocalPort = (uint)connection.LocalPort,
            RemoteAddress = connection.RemoteAddress,
            RemotePort = (uint)connection.RemotePort,
            State = connection.State,
            Pid = (uint)(connection.Pid ?? 0),
            ProcessName = connection.ProcessName ?? string.Empty,
        }));

        return reply;
    }

    public override async Task<DeviceHistoryReply> GetDeviceHistory(GetDeviceHistoryRequest request, ServerCallContext context)
    {
        if (request.From is null || request.To is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The period must have a start and an end."));
        }

        DeviceHistoryDto? history;

        try
        {
            history = await AuthorizeForDeviceAsync(context, Permission.ViewDevices, request.Id) is { } id
                ? await _historyService.GetHistoryAsync(id, request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset(), context.CancellationToken)
                : null;
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        if (history is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."));
        }

        var reply = new DeviceHistoryReply
        {
            DowntimeSeconds = (long)history.Downtime.TotalSeconds,
            EventsTruncated = history.EventsTruncated,
        };

        if (history.AvailabilityPercent is { } availabilityPercent)
        {
            reply.AvailabilityPercent = availabilityPercent;
        }

        reply.Outages.AddRange(history.Outages.Select(outage => new OutageInfo
        {
            StartedAt = Timestamp.FromDateTimeOffset(outage.StartedAt),
            EndedAt = outage.EndedAt is { } endedAt ? Timestamp.FromDateTimeOffset(endedAt) : null,
        }));
        reply.Events.AddRange(history.Events.Select(monitoringEvent => new EventInfo
        {
            OccurredAt = Timestamp.FromDateTimeOffset(monitoringEvent.OccurredAt),
            Type = monitoringEvent.Type.ToString(),
            Severity = monitoringEvent.Severity switch
            {
                DomainEventSeverity.Info => EventSeverity.Info,
                DomainEventSeverity.Warning => EventSeverity.Warning,
                DomainEventSeverity.Error => EventSeverity.Error,
                _ => EventSeverity.Unspecified,
            },
            Message = monitoringEvent.Message,
        }));
        reply.ResourcePoints.AddRange(history.ResourcePoints.Select(point =>
        {
            var info = new ResourcePointInfo
            {
                RecordedAt = Timestamp.FromDateTimeOffset(point.RecordedAt),
                MemoryUsedBytes = (ulong)point.MemoryUsedBytes,
                MemoryTotalBytes = (ulong)point.MemoryTotalBytes,
            };

            if (point.CpuUsagePercent is { } cpuUsagePercent)
            {
                info.CpuUsagePercent = cpuUsagePercent;
            }

            return info;
        }));

        return reply;
    }

    public override async Task<MonitoringSettingsInfo> GetMonitoringSettings(GetMonitoringSettingsRequest request, ServerCallContext context)
    {
        return ToSettingsInfo(await _settingsService.GetAsync(context.CancellationToken));
    }

    public override async Task<MonitoringSettingsInfo> UpdateSyncInterval(UpdateSyncIntervalRequest request, ServerCallContext context)
    {
        await RequireAsync(context, Permission.ManageSettings);

        try
        {
            return ToSettingsInfo(
                await _settingsService.ChangeSyncIntervalAsync(request.SyncIntervalSeconds, context.CancellationToken));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    /// <summary>Refuses the call unless the user holds the permission for all devices.</summary>
    private async Task RequireAsync(ServerCallContext context, Permission permission)
    {
        var user = AdminAuthenticationInterceptor.GetUser(context);

        if (!user.Can(permission))
        {
            throw await DenyAsync(context, user, deviceId: null);
        }
    }

    /// <summary>Refuses the call unless the user holds the permission for the device.</summary>
    /// <returns>The device identifier, or null when the text is not one and so cannot name any device.</returns>
    private async Task<Guid?> AuthorizeForDeviceAsync(ServerCallContext context, Permission permission, string deviceIdText)
    {
        var user = AdminAuthenticationInterceptor.GetUser(context);

        if (!Guid.TryParse(deviceIdText, out var deviceId))
        {
            return user.CanOnSomeDevice(permission) ? null : throw await DenyAsync(context, user, deviceId: null);
        }

        // Checked before the device is looked up, so a refusal does not reveal whether the device exists.
        if (!user.CanOnDevice(permission, deviceId))
        {
            throw await DenyAsync(context, user, deviceId);
        }

        return deviceId;
    }

    private async Task<RpcException> DenyAsync(ServerCallContext context, AuthenticatedUser user, Guid? deviceId)
    {
        var operation = context.Method[(context.Method.LastIndexOf('/') + 1)..];

        // A client that keeps repeating a refused call must not fill the event log.
        if (_deniedAccessLog.ShouldRecord(user.Id, operation, deviceId))
        {
            // The device may not exist; an event must not point to a missing one.
            var existingDeviceId = deviceId is { } id && await _deviceService.GetDeviceAsync(id, context.CancellationToken) is not null
                ? deviceId
                : null;

            await _authenticationService.RecordAccessDeniedAsync(user, operation, existingDeviceId, context.CancellationToken);
        }

        return new RpcException(new Status(StatusCode.PermissionDenied, "You do not have permission for this operation."));
    }

    private static DeviceInfo ToDeviceInfo(DeviceDto device)
    {
        return new DeviceInfo
        {
            Id = device.Id.ToString(),
            Name = device.Name,
            MonitoringMode = device.MonitoringMode switch
            {
                DomainMonitoringMode.Agent => MonitoringMode.Agent,
                DomainMonitoringMode.Agentless => MonitoringMode.Agentless,
                _ => MonitoringMode.Unspecified,
            },
            HostName = device.HostName ?? string.Empty,
            IpAddress = device.IpAddress ?? string.Empty,
            Status = device.Status switch
            {
                DomainDeviceStatus.Online => DeviceStatus.Online,
                DomainDeviceStatus.Offline => DeviceStatus.Offline,
                DomainDeviceStatus.Unknown => DeviceStatus.Unknown,
                _ => DeviceStatus.Unspecified,
            },
            LastSeenAt = device.LastSeenAt is { } lastSeenAt ? Timestamp.FromDateTimeOffset(lastSeenAt) : null,
            IsEnabled = device.IsEnabled,
            OperatingSystem = device.OperatingSystem ?? string.Empty,
        };
    }

    private static MonitoringSettingsInfo ToSettingsInfo(MonitoringSettingsDto settings)
    {
        return new MonitoringSettingsInfo
        {
            SyncIntervalSeconds = settings.SyncIntervalSeconds,
            OfflineAfterMissedSyncs = settings.OfflineAfterMissedSyncs,
            MinSyncIntervalSeconds = settings.MinSyncIntervalSeconds,
            MaxSyncIntervalSeconds = settings.MaxSyncIntervalSeconds,
        };
    }
}
