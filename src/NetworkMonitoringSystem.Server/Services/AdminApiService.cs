using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Commands;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Commands;
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
    private readonly IAccessManagementService _accessManagement;
    private readonly IRemoteCommandService _commandService;
    private readonly IEventLogService _eventLog;
    private readonly IEnrollmentCodeService _enrollmentCodes;

    public AdminApiService(
        IDeviceService deviceService,
        IMonitoringSettingsService settingsService,
        IDeviceHistoryService historyService,
        IAuthenticationService authenticationService,
        IAccessManagementService accessManagement,
        IRemoteCommandService commandService,
        IEventLogService eventLog,
        IEnrollmentCodeService enrollmentCodes,
        DeniedAccessLog deniedAccessLog)
    {
        _commandService = commandService;
        _eventLog = eventLog;
        _enrollmentCodes = enrollmentCodes;
        _accessManagement = accessManagement;
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

    public override async Task<ChangeOwnPasswordReply> ChangeOwnPassword(ChangeOwnPasswordRequest request, ServerCallContext context)
    {
        var user = AdminAuthenticationInterceptor.GetUser(context);

        await ManageAsync(() => _accessManagement.ChangeOwnPasswordAsync(user, request.CurrentPassword, request.NewPassword, context.CancellationToken));

        return new ChangeOwnPasswordReply();
    }

    public override async Task<ListUsersReply> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
        await RequireAsync(context, Permission.ManageUsers);

        var reply = new ListUsersReply();
        reply.Users.AddRange((await _accessManagement.GetUsersAsync(context.CancellationToken)).Select(ToUserInfo));

        return reply;
    }

    public override async Task<UserInfo> CreateUser(CreateUserRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);

        return ToUserInfo(await ManageAsync(() => _accessManagement.CreateUserAsync(
            actor, request.UserName, request.Password, ParseIds(request.RoleIds), context.CancellationToken)));
    }

    public override async Task<UserInfo> SetUserActive(SetUserActiveRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);

        return ToUserInfo(await ManageAsync(() => _accessManagement.SetUserActiveAsync(
            actor, ParseId(request.UserId), request.IsActive, context.CancellationToken)));
    }

    public override async Task<UserInfo> SetUserRoles(SetUserRolesRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);

        return ToUserInfo(await ManageAsync(() => _accessManagement.SetUserRolesAsync(
            actor, ParseId(request.UserId), ParseIds(request.RoleIds), context.CancellationToken)));
    }

    public override async Task<ResetUserPasswordReply> ResetUserPassword(ResetUserPasswordRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);

        await ManageAsync(() => _accessManagement.ResetPasswordAsync(actor, ParseId(request.UserId), request.NewPassword, context.CancellationToken));

        return new ResetUserPasswordReply();
    }

    public override async Task<ListRolesReply> ListRoles(ListRolesRequest request, ServerCallContext context)
    {
        await RequireAsync(context, Permission.ManageUsers);

        var reply = new ListRolesReply();
        reply.Roles.AddRange((await _accessManagement.GetRolesAsync(context.CancellationToken)).Select(ToRoleInfo));
        reply.Permissions.AddRange(System.Enum.GetNames<Permission>());

        return reply;
    }

    public override async Task<RoleInfo> CreateRole(CreateRoleRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);

        return ToRoleInfo(await ManageAsync(() => _accessManagement.CreateRoleAsync(actor, request.Name, context.CancellationToken)));
    }

    public override async Task<DeleteUserReply> DeleteUser(DeleteUserRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);

        await ManageAsync(() => _accessManagement.DeleteUserAsync(actor, ParseId(request.UserId), context.CancellationToken));

        return new DeleteUserReply();
    }

    public override async Task<DeleteRoleReply> DeleteRole(DeleteRoleRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);

        await ManageAsync(() => _accessManagement.DeleteRoleAsync(actor, ParseId(request.RoleId), context.CancellationToken));

        return new DeleteRoleReply();
    }

    public override async Task<RoleInfo> AddAccessRule(AccessRuleRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);
        var (permission, deviceId) = ParseRule(request.Rule);

        return ToRoleInfo(await ManageAsync(() => _accessManagement.AllowAsync(
            actor, ParseId(request.RoleId), permission, deviceId, context.CancellationToken)));
    }

    public override async Task<RoleInfo> RemoveAccessRule(AccessRuleRequest request, ServerCallContext context)
    {
        var actor = await RequireAsync(context, Permission.ManageUsers);
        var (permission, deviceId) = ParseRule(request.Rule);

        return ToRoleInfo(await ManageAsync(() => _accessManagement.RevokeAsync(
            actor, ParseId(request.RoleId), permission, deviceId, context.CancellationToken)));
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
        reply.Devices.AddRange(devices.Where(device => user.CanOnDevice(Permission.ViewDevices, device.Id)).Select(device => ToDeviceInfo(device, user)));

        return reply;
    }

    public override async Task<DeviceInfo> AddAgentlessDevice(AddAgentlessDeviceRequest request, ServerCallContext context)
    {
        await RequireAsync(context, Permission.ManageDevices);

        try
        {
            var device = await _deviceService.RegisterDeviceAsync(
                new RegisterDeviceRequest(
                    request.Name,
                    DomainMonitoringMode.Agentless,
                    IpAddress: request.IpAddress,
                    TcpPort: request.HasTcpPort ? request.TcpPort : null),
                context.CancellationToken);

            return ToDeviceInfo(device, AdminAuthenticationInterceptor.GetUser(context));
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

    public override async Task<DeviceInfo> SetDeviceEnabled(SetDeviceEnabledRequest request, ServerCallContext context)
    {
        var id = await AuthorizeForDeviceAsync(context, Permission.ManageDevices, request.Id);
        var user = AdminAuthenticationInterceptor.GetUser(context);

        var device = id is null
            ? null
            : await _deviceService.SetDeviceEnabledAsync(id.Value, request.IsEnabled, user.Id, context.CancellationToken);

        return device is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."))
            : ToDeviceInfo(device, AdminAuthenticationInterceptor.GetUser(context));
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

    public override async Task<DeviceActivityHistoryReply> GetDeviceActivityHistory(GetDeviceActivityHistoryRequest request, ServerCallContext context)
    {
        if (request.From is null || request.To is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The period must have a start and an end."));
        }

        var reply = new DeviceActivityHistoryReply();

        if (await AuthorizeForDeviceAsync(context, Permission.ViewDevices, request.Id) is not { } id)
        {
            return reply;
        }

        ActivityHistoryDto history;

        try
        {
            history = await _deviceService.GetActivityHistoryAsync(
                id, request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset(), context.CancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        reply.Truncated = history.Truncated;
        reply.Processes.AddRange(history.Processes.Select(run => new ProcessRunInfo
        {
            Pid = (uint)run.Pid,
            Name = run.Name,
            StartedAt = Timestamp.FromDateTimeOffset(run.StartedAt),
            EndedAt = run.EndedAt is { } endedAt ? Timestamp.FromDateTimeOffset(endedAt) : null,
        }));
        reply.Ports.AddRange(history.Ports.Select(port => new PortPeriodInfo
        {
            Protocol = port.Protocol.ToString().ToUpperInvariant(),
            LocalAddress = port.LocalAddress,
            Port = (uint)port.Port,
            OpenedAt = Timestamp.FromDateTimeOffset(port.OpenedAt),
            ClosedAt = port.ClosedAt is { } closedAt ? Timestamp.FromDateTimeOffset(closedAt) : null,
            Pid = (uint)(port.Process?.Pid ?? 0),
            ProcessName = port.Process?.Name ?? string.Empty,
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

    public override async Task<DeviceInfo> RenameDevice(RenameDeviceRequest request, ServerCallContext context)
    {
        var id = await AuthorizeForDeviceAsync(context, Permission.ManageDevices, request.Id);
        var user = AdminAuthenticationInterceptor.GetUser(context);
        DeviceDto? device;

        try
        {
            device = id is null ? null : await _deviceService.RenameDeviceAsync(id.Value, request.Name, user.Id, context.CancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        return device is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."))
            : ToDeviceInfo(device, AdminAuthenticationInterceptor.GetUser(context));
    }

    /// <summary>
    /// A code for a new computer adds a device, so it needs the right to manage all devices; a code that registers an
    /// existing device again needs the right to manage that device.
    /// </summary>
    public override async Task<CreatedEnrollmentCodeReply> CreateEnrollmentCode(CreateEnrollmentCodeRequest request, ServerCallContext context)
    {
        Guid? deviceId = null;

        if (string.IsNullOrEmpty(request.DeviceId))
        {
            await RequireAsync(context, Permission.ManageDevices);
        }
        else
        {
            deviceId = await AuthorizeForDeviceAsync(context, Permission.ManageDevices, request.DeviceId)
                ?? throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."));
        }

        var user = AdminAuthenticationInterceptor.GetUser(context);
        CreatedEnrollmentCode? created;

        try
        {
            created = await _enrollmentCodes.CreateAsync(user, request.Note, deviceId, TimeSpan.FromMinutes(request.ValidMinutes), context.CancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        return created is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."))
            : new CreatedEnrollmentCodeReply { Code = created.Code, Info = ToEnrollmentCodeInfo(created.Info) };
    }

    public override async Task<ListEnrollmentCodesReply> ListEnrollmentCodes(ListEnrollmentCodesRequest request, ServerCallContext context)
    {
        var user = AdminAuthenticationInterceptor.GetUser(context);

        if (!user.CanOnSomeDevice(Permission.ManageDevices))
        {
            throw await DenyAsync(context, user, deviceId: null);
        }

        var reply = new ListEnrollmentCodesReply();
        reply.Codes.AddRange((await _enrollmentCodes.GetRecentAsync(context.CancellationToken))
            .Where(code => CanManageCode(user, code.DeviceId))
            .Select(ToEnrollmentCodeInfo));

        return reply;
    }

    public override async Task<EnrollmentCodeInfo> RevokeEnrollmentCode(RevokeEnrollmentCodeRequest request, ServerCallContext context)
    {
        var user = AdminAuthenticationInterceptor.GetUser(context);

        if (!user.CanOnSomeDevice(Permission.ManageDevices))
        {
            throw await DenyAsync(context, user, deviceId: null);
        }

        // Which device a code belongs to is known only once it is found, so the permission is checked after that.
        var id = ParseId(request.Id);
        var code = (await _enrollmentCodes.GetRecentAsync(context.CancellationToken)).FirstOrDefault(listed => listed.Id == id);

        if (code is null || !CanManageCode(user, code.DeviceId))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "The item does not exist."));
        }

        var revoked = await _enrollmentCodes.RevokeAsync(user, id, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "The item does not exist."));

        return ToEnrollmentCodeInfo(revoked);
    }

    private static bool CanManageCode(AuthenticatedUser user, Guid? deviceId)
    {
        return deviceId is { } id ? user.CanOnDevice(Permission.ManageDevices, id) : user.Can(Permission.ManageDevices);
    }

    private static EnrollmentCodeInfo ToEnrollmentCodeInfo(EnrollmentCodeDto code)
    {
        var info = new EnrollmentCodeInfo
        {
            Id = code.Id.ToString(),
            Note = code.Note ?? string.Empty,
            DeviceId = code.DeviceId?.ToString() ?? string.Empty,
            DeviceName = code.DeviceName ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(code.CreatedAt),
            ExpiresAt = Timestamp.FromDateTimeOffset(code.ExpiresAt),
            CreatedBy = code.CreatedByUserName,
            State = code.State.ToString(),
            UsedByHostName = code.UsedByHostName ?? string.Empty,
        };

        if (code.UsedAt is { } usedAt)
        {
            info.UsedAt = Timestamp.FromDateTimeOffset(usedAt);
        }

        return info;
    }

    public override async Task<DeviceInfo> SetDeviceTrafficCapture(SetDeviceTrafficCaptureRequest request, ServerCallContext context)
    {
        var id = await AuthorizeForDeviceAsync(context, Permission.ManageDevices, request.Id);
        var user = AdminAuthenticationInterceptor.GetUser(context);
        DeviceDto? device;

        try
        {
            device = id is null
                ? null
                : await _deviceService.SetTrafficCaptureAsync(id.Value, request.Capture, user.Id, context.CancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        return device is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."))
            : ToDeviceInfo(device, AdminAuthenticationInterceptor.GetUser(context));
    }

    public override async Task<EventLogReply> GetEventLog(GetEventLogRequest request, ServerCallContext context)
    {
        await RequireAsync(context, Permission.ViewEventLog);

        if (request.From is null || request.To is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The period must have a start and an end."));
        }

        EventLogDto log;

        try
        {
            log = await _eventLog.GetAsync(request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset(), context.CancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        var reply = new EventLogReply { Truncated = log.Truncated };
        reply.Events.AddRange(log.Events.Select(entry => new EventLogEntry
        {
            OccurredAt = Timestamp.FromDateTimeOffset(entry.OccurredAt),
            Type = entry.Type.ToString(),
            Severity = ToSeverity(entry.Severity),
            Message = entry.Message,
            DeviceName = entry.DeviceName ?? string.Empty,
            UserName = entry.UserName ?? string.Empty,
        }));

        return reply;
    }

    public override async Task<DeviceTrafficReply> GetDeviceTraffic(GetDeviceTrafficRequest request, ServerCallContext context)
    {
        if (request.From is null || request.To is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The period must have a start and an end."));
        }

        var reply = new DeviceTrafficReply();

        if (await AuthorizeForDeviceAsync(context, Permission.ViewDevices, request.Id) is not { } id)
        {
            return reply;
        }

        try
        {
            var entries = await _deviceService.GetTrafficAsync(
                id, request.From.ToDateTimeOffset(), request.To.ToDateTimeOffset(), context.CancellationToken);

            reply.Entries.AddRange(entries.Select(entry =>
            {
                var info = new TrafficEntry
                {
                    Transport = entry.Transport.ToString(),
                    Protocol = entry.Protocol,
                    RemoteAddress = entry.RemoteAddress,
                    PacketsSent = entry.PacketsSent,
                    PacketsReceived = entry.PacketsReceived,
                    BytesSent = entry.BytesSent,
                    BytesReceived = entry.BytesReceived,
                };

                if (entry.ServicePort is { } port)
                {
                    info.ServicePort = port;
                }

                return info;
            }));
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

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
            Severity = ToSeverity(monitoringEvent.Severity),
            Message = monitoringEvent.Message,
        }));
        reply.NetworkPoints.AddRange(history.NetworkPoints.Select(point =>
        {
            var info = new NetworkPointInfo { RecordedAt = Timestamp.FromDateTimeOffset(point.RecordedAt) };

            if (point.BytesSentPerSecond is { } sent)
            {
                info.BytesSentPerSecond = sent;
            }

            if (point.BytesReceivedPerSecond is { } received)
            {
                info.BytesReceivedPerSecond = received;
            }

            if (point.ResponseTimeMs is { } responseTime)
            {
                info.ResponseTimeMs = responseTime;
            }

            return info;
        }));

        return reply;
    }

    public override async Task<CommandInfo> SendCommand(SendCommandRequest request, ServerCallContext context)
    {
        if (!System.Enum.TryParse<RemoteCommandType>(request.Type, out var type) || !System.Enum.IsDefined(type))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Unknown command."));
        }

        var user = AdminAuthenticationInterceptor.GetUser(context);
        var deviceId = await AuthorizeForDeviceAsync(context, RemoteCommand.PermissionFor(type), request.DeviceId);

        try
        {
            var command = deviceId is { } id
                ? await _commandService.RequestAsync(user, id, type, request.Parameter, context.CancellationToken)
                : null;

            return command is null
                ? throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."))
                : ToCommandInfo(command);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (RemoteCommandRefusedException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<CommandInfo> SendFile(IAsyncStreamReader<SendFileRequest> requestStream, ServerCallContext context)
    {
        if (!await requestStream.MoveNext(context.CancellationToken) || requestStream.Current.PartCase != SendFileRequest.PartOneofCase.Header)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The upload must start with the header of the file."));
        }

        var header = requestStream.Current.Header;
        var user = AdminAuthenticationInterceptor.GetUser(context);

        // The permission is checked before the contents are read, so a refused user cannot make the server receive them.
        var deviceId = await AuthorizeForDeviceAsync(context, Permission.SendFile, header.DeviceId);

        if (deviceId is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."));
        }

        if (header.Size is < 0 or > FileTransfer.MaxSize)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"A file can have at most {FileTransfer.MaxSize / (1024 * 1024)} MB."));
        }

        try
        {
            FileTransfer.NormalizeFileName(header.FileName);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        using var content = new MemoryStream((int)header.Size);

        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var data = requestStream.Current.Data;

            if (requestStream.Current.PartCase != SendFileRequest.PartOneofCase.Data || content.Length + data.Length > header.Size)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The contents do not match the size in the header."));
            }

            data.WriteTo(content);
        }

        if (content.Length != header.Size)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The contents do not match the size in the header."));
        }

        try
        {
            var command = await _commandService.SendFileAsync(user, deviceId.Value, header.FileName, content.ToArray(), context.CancellationToken);

            return command is null
                ? throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."))
                : ToCommandInfo(command);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (RemoteCommandRefusedException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<ListDeviceCommandsReply> ListDeviceCommands(ListDeviceCommandsRequest request, ServerCallContext context)
    {
        var reply = new ListDeviceCommandsReply();

        if (await AuthorizeForDeviceAsync(context, Permission.ViewDevices, request.DeviceId) is { } id)
        {
            reply.Commands.AddRange((await _commandService.GetRecentAsync(id, context.CancellationToken)).Select(ToCommandInfo));
        }

        return reply;
    }

    private static CommandInfo ToCommandInfo(RemoteCommandDto command)
    {
        return new CommandInfo
        {
            Id = command.Id.ToString(),
            Type = command.Type.ToString(),
            Parameter = command.Parameter ?? string.Empty,
            Status = command.Status.ToString(),
            RequestedBy = command.RequestedByUserName,
            RequestedAt = Timestamp.FromDateTimeOffset(command.RequestedAt),
            CompletedAt = command.CompletedAt is { } completedAt ? Timestamp.FromDateTimeOffset(completedAt) : null,
            ResultMessage = command.ResultMessage ?? string.Empty,
        };
    }

    public override async Task<MonitoringSettingsInfo> GetMonitoringSettings(GetMonitoringSettingsRequest request, ServerCallContext context)
    {
        return ToSettingsInfo(await _settingsService.GetAsync(context.CancellationToken));
    }

    public override Task<MonitoringSettingsInfo> UpdateSyncInterval(UpdateSyncIntervalRequest request, ServerCallContext context)
    {
        return ChangeSettingAsync(context, userId => _settingsService.ChangeSyncIntervalAsync(request.SyncIntervalSeconds, userId, context.CancellationToken));
    }

    public override Task<MonitoringSettingsInfo> UpdateOfflineThreshold(UpdateOfflineThresholdRequest request, ServerCallContext context)
    {
        return ChangeSettingAsync(context, userId => _settingsService.ChangeOfflineThresholdAsync(request.OfflineAfterMissedSyncs, userId, context.CancellationToken));
    }

    public override Task<MonitoringSettingsInfo> UpdateRetention(UpdateRetentionRequest request, ServerCallContext context)
    {
        return ChangeSettingAsync(context, userId => _settingsService.ChangeRetentionAsync(request.RetentionDays, userId, context.CancellationToken));
    }

    /// <summary>Changes one monitoring setting for a user who may manage settings; a value out of range is invalid input.</summary>
    private async Task<MonitoringSettingsInfo> ChangeSettingAsync(ServerCallContext context, Func<Guid, Task<MonitoringSettingsDto>> change)
    {
        var user = await RequireAsync(context, Permission.ManageSettings);

        try
        {
            return ToSettingsInfo(await change(user.Id));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    /// <summary>Runs a management operation and turns its failures into the matching gRPC status.</summary>
    private static async Task<T> ManageAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (AccessItemNotFoundException exception)
        {
            throw new RpcException(new Status(StatusCode.NotFound, exception.Message));
        }
        catch (AccessChangeRefusedException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
        catch (AuthenticationFailedException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The current password is not correct."));
        }
    }

    private static Task ManageAsync(Func<Task> operation)
    {
        return ManageAsync(async () =>
        {
            await operation();

            return true;
        });
    }

    private static Guid ParseId(string text)
    {
        return Guid.TryParse(text, out var id)
            ? id
            : throw new RpcException(new Status(StatusCode.NotFound, "The item does not exist."));
    }

    private static List<Guid> ParseIds(IEnumerable<string> texts) => texts.Select(ParseId).ToList();

    private static (Permission Permission, Guid? DeviceId) ParseRule(AccessRuleInfo? rule)
    {
        if (rule is null || !System.Enum.TryParse<Permission>(rule.Permission, out var permission) || !System.Enum.IsDefined(permission))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Unknown permission."));
        }

        return (permission, string.IsNullOrEmpty(rule.DeviceId) ? null : ParseId(rule.DeviceId));
    }

    private static UserInfo ToUserInfo(UserDto user)
    {
        var info = new UserInfo
        {
            Id = user.Id.ToString(),
            UserName = user.UserName,
            IsActive = user.IsActive,
            IsLocked = user.IsLocked,
            CreatedAt = Timestamp.FromDateTimeOffset(user.CreatedAt),
        };
        info.RoleIds.AddRange(user.RoleIds.Select(roleId => roleId.ToString()));

        return info;
    }

    private static RoleInfo ToRoleInfo(RoleDto role)
    {
        var info = new RoleInfo { Id = role.Id.ToString(), Name = role.Name, IsBuiltIn = role.IsBuiltIn };
        info.Rules.AddRange(role.Rules.Select(rule => new AccessRuleInfo
        {
            Permission = rule.Permission.ToString(),
            DeviceId = rule.DeviceId?.ToString() ?? string.Empty,
        }));

        return info;
    }

    /// <summary>Refuses the call unless the user holds the permission for all devices.</summary>
    private async Task<AuthenticatedUser> RequireAsync(ServerCallContext context, Permission permission)
    {
        var user = AdminAuthenticationInterceptor.GetUser(context);

        if (!user.Can(permission))
        {
            throw await DenyAsync(context, user, deviceId: null);
        }

        return user;
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

    private static EventSeverity ToSeverity(DomainEventSeverity severity)
    {
        return severity switch
        {
            DomainEventSeverity.Info => EventSeverity.Info,
            DomainEventSeverity.Warning => EventSeverity.Warning,
            DomainEventSeverity.Error => EventSeverity.Error,
            _ => EventSeverity.Unspecified,
        };
    }

    /// <summary>The device as the given user sees it, with the permissions the user holds on it.</summary>
    private static DeviceInfo ToDeviceInfo(DeviceDto device, AuthenticatedUser user)
    {
        var info = new DeviceInfo
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
            CaptureTraffic = device.CaptureTraffic,
            TrafficCaptureProblem = device.TrafficCaptureProblem ?? string.Empty,
        };

        // Only for showing what the user may do; every operation is still checked on its own.
        info.Permissions.AddRange(System.Enum.GetValues<Permission>()
            .Where(permission => user.CanOnDevice(permission, device.Id))
            .Select(permission => permission.ToString()));

        if (device.TcpPort is { } tcpPort)
        {
            info.TcpPort = tcpPort;
        }

        return info;
    }

    private static MonitoringSettingsInfo ToSettingsInfo(MonitoringSettingsDto settings)
    {
        return new MonitoringSettingsInfo
        {
            SyncIntervalSeconds = settings.SyncIntervalSeconds,
            OfflineAfterMissedSyncs = settings.OfflineAfterMissedSyncs,
            MinSyncIntervalSeconds = settings.MinSyncIntervalSeconds,
            MaxSyncIntervalSeconds = settings.MaxSyncIntervalSeconds,
            RetentionDays = settings.RetentionDays,
            MaxOfflineAfterMissedSyncs = settings.MaxOfflineAfterMissedSyncs,
            MaxRetentionDays = settings.MaxRetentionDays,
        };
    }
}
