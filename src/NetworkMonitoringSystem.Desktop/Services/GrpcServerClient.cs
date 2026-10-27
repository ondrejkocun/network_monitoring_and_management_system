using System.IO;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.Services;

public sealed class GrpcServerClient : IServerClient
{
    private readonly AdminApi.AdminApiClient _client;
    private string? _token;

    public GrpcServerClient(AdminApi.AdminApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    public async Task<LoginReply> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(() => _client.LoginAsync(
            new LoginRequest { UserName = userName, Password = password },
            cancellationToken: cancellationToken));

        _token = reply.Token;

        return reply;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (_token is null)
        {
            return;
        }

        try
        {
            await _client.LogoutAsync(new LogoutRequest(), Headers(), cancellationToken: cancellationToken);
        }
        catch (RpcException)
        {
            // The session ends on its own when it expires; the user must be able to sign out regardless.
        }
        finally
        {
            _token = null;
        }
    }

    public async Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.ChangeOwnPasswordAsync(
            new ChangeOwnPasswordRequest { CurrentPassword = currentPassword, NewPassword = newPassword },
            Headers(), cancellationToken: cancellationToken));

        _token = null;
    }

    public async Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return (await CallAsync(() => _client.ListUsersAsync(new ListUsersRequest(), Headers(), cancellationToken: cancellationToken))).Users;
    }

    public Task<ListRolesReply> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.ListRolesAsync(new ListRolesRequest(), Headers(), cancellationToken: cancellationToken));
    }

    public async Task CreateUserAsync(string userName, string password, IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken = default)
    {
        var request = new CreateUserRequest { UserName = userName, Password = password };
        request.RoleIds.AddRange(roleIds);

        await CallAsync(() => _client.CreateUserAsync(request, Headers(), cancellationToken: cancellationToken));
    }

    public async Task SetUserActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.SetUserActiveAsync(
            new SetUserActiveRequest { UserId = userId, IsActive = isActive },
            Headers(), cancellationToken: cancellationToken));
    }

    public async Task DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.DeleteUserAsync(new DeleteUserRequest { UserId = userId }, Headers(), cancellationToken: cancellationToken));
    }

    public async Task SetUserRolesAsync(string userId, IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken = default)
    {
        var request = new SetUserRolesRequest { UserId = userId };
        request.RoleIds.AddRange(roleIds);

        await CallAsync(() => _client.SetUserRolesAsync(request, Headers(), cancellationToken: cancellationToken));
    }

    public async Task ResetUserPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.ResetUserPasswordAsync(
            new ResetUserPasswordRequest { UserId = userId, NewPassword = newPassword },
            Headers(), cancellationToken: cancellationToken));
    }

    public async Task CreateRoleAsync(string name, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.CreateRoleAsync(new CreateRoleRequest { Name = name }, Headers(), cancellationToken: cancellationToken));
    }

    public async Task DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.DeleteRoleAsync(new DeleteRoleRequest { RoleId = roleId }, Headers(), cancellationToken: cancellationToken));
    }

    public async Task AddAccessRuleAsync(string roleId, string permission, string deviceId, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.AddAccessRuleAsync(CreateRuleRequest(roleId, permission, deviceId), Headers(), cancellationToken: cancellationToken));
    }

    public async Task RemoveAccessRuleAsync(string roleId, string permission, string deviceId, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.RemoveAccessRuleAsync(CreateRuleRequest(roleId, permission, deviceId), Headers(), cancellationToken: cancellationToken));
    }

    private static AccessRuleRequest CreateRuleRequest(string roleId, string permission, string deviceId)
    {
        return new AccessRuleRequest
        {
            RoleId = roleId,
            Rule = new AccessRuleInfo { Permission = permission, DeviceId = deviceId },
        };
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(() => _client.ListDevicesAsync(new ListDevicesRequest(), Headers(), cancellationToken: cancellationToken));

        return reply.Devices;
    }

    public async Task AddAgentlessDeviceAsync(string name, string ipAddress, int? tcpPort = null, CancellationToken cancellationToken = default)
    {
        var request = new AddAgentlessDeviceRequest { Name = name, IpAddress = ipAddress };

        if (tcpPort is { } port)
        {
            request.TcpPort = port;
        }

        await CallAsync(() => _client.AddAgentlessDeviceAsync(request, Headers(), cancellationToken: cancellationToken));
    }

    public Task<DeviceResourcesReply> GetDeviceResourcesAsync(string id, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetDeviceResourcesAsync(new GetDeviceResourcesRequest { Id = id }, Headers(), cancellationToken: cancellationToken));
    }

    public Task<DeviceActivityReply> GetDeviceActivityAsync(string id, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetDeviceActivityAsync(new GetDeviceActivityRequest { Id = id }, Headers(), cancellationToken: cancellationToken));
    }

    public Task<DeviceHistoryReply> GetDeviceHistoryAsync(string id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetDeviceHistoryAsync(
            new GetDeviceHistoryRequest
            {
                Id = id,
                From = Timestamp.FromDateTimeOffset(from),
                To = Timestamp.FromDateTimeOffset(to),
            },
            Headers(), cancellationToken: cancellationToken));
    }

    public async Task SendCommandAsync(string deviceId, string type, string parameter, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.SendCommandAsync(
            new SendCommandRequest { DeviceId = deviceId, Type = type, Parameter = parameter },
            Headers(), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<CommandInfo>> GetDeviceCommandsAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(() => _client.ListDeviceCommandsAsync(
            new ListDeviceCommandsRequest { DeviceId = deviceId },
            Headers(), cancellationToken: cancellationToken));

        return reply.Commands;
    }

    public async Task RemoveDeviceAsync(string id, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.RemoveDeviceAsync(new RemoveDeviceRequest { Id = id }, Headers(), cancellationToken: cancellationToken));
    }

    public Task<DeviceInfo> SetDeviceEnabledAsync(string id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.SetDeviceEnabledAsync(
            new SetDeviceEnabledRequest { Id = id, IsEnabled = isEnabled },
            Headers(), cancellationToken: cancellationToken));
    }

    public Task<DeviceInfo> RenameDeviceAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.RenameDeviceAsync(new RenameDeviceRequest { Id = id, Name = name }, Headers(), cancellationToken: cancellationToken));
    }

    public Task<CreatedEnrollmentCodeReply> CreateEnrollmentCodeAsync(string note, string? deviceId, int validMinutes, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.CreateEnrollmentCodeAsync(
            new CreateEnrollmentCodeRequest { Note = note, DeviceId = deviceId ?? string.Empty, ValidMinutes = validMinutes },
            Headers(), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<EnrollmentCodeInfo>> GetEnrollmentCodesAsync(CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(() => _client.ListEnrollmentCodesAsync(new ListEnrollmentCodesRequest(), Headers(), cancellationToken: cancellationToken));

        return reply.Codes;
    }

    public Task<EnrollmentCodeInfo> RevokeEnrollmentCodeAsync(string id, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.RevokeEnrollmentCodeAsync(new RevokeEnrollmentCodeRequest { Id = id }, Headers(), cancellationToken: cancellationToken));
    }

    public Task<DeviceInfo> SetDeviceTrafficCaptureAsync(string id, bool capture, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.SetDeviceTrafficCaptureAsync(
            new SetDeviceTrafficCaptureRequest { Id = id, Capture = capture },
            Headers(), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TrafficEntry>> GetDeviceTrafficAsync(string id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(() => _client.GetDeviceTrafficAsync(
            new GetDeviceTrafficRequest { Id = id, From = Timestamp.FromDateTimeOffset(from), To = Timestamp.FromDateTimeOffset(to) },
            Headers(), cancellationToken: cancellationToken));

        return reply.Entries;
    }

    public Task<MonitoringSettingsInfo> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetMonitoringSettingsAsync(new GetMonitoringSettingsRequest(), Headers(), cancellationToken: cancellationToken));
    }

    public Task<DeviceActivityHistoryReply> GetDeviceActivityHistoryAsync(string id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetDeviceActivityHistoryAsync(
            new GetDeviceActivityHistoryRequest { Id = id, From = Timestamp.FromDateTimeOffset(from), To = Timestamp.FromDateTimeOffset(to) },
            Headers(), cancellationToken: cancellationToken));
    }

    public Task<EventLogReply> GetEventLogAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetEventLogAsync(
            new GetEventLogRequest { From = Timestamp.FromDateTimeOffset(from), To = Timestamp.FromDateTimeOffset(to) },
            Headers(), cancellationToken: cancellationToken));
    }

    public Task<MonitoringSettingsInfo> UpdateSyncIntervalAsync(int seconds, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.UpdateSyncIntervalAsync(
            new UpdateSyncIntervalRequest { SyncIntervalSeconds = seconds },
            Headers(), cancellationToken: cancellationToken));
    }

    public Task<MonitoringSettingsInfo> UpdateOfflineThresholdAsync(int missedSyncs, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.UpdateOfflineThresholdAsync(
            new UpdateOfflineThresholdRequest { OfflineAfterMissedSyncs = missedSyncs },
            Headers(), cancellationToken: cancellationToken));
    }

    public Task<MonitoringSettingsInfo> UpdateRetentionAsync(int days, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.UpdateRetentionAsync(
            new UpdateRetentionRequest { RetentionDays = days },
            Headers(), cancellationToken: cancellationToken));
    }

    public async Task<CommandInfo> SendFileAsync(string deviceId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var call = _client.SendFile(Headers(), cancellationToken: cancellationToken);

        try
        {
            try
            {
                await call.RequestStream.WriteAsync(new SendFileRequest
                {
                    Header = new SendFileHeader { DeviceId = deviceId, FileName = fileName, Size = content.Length },
                });

                // The server takes parts of at most 1 MB.
                var buffer = new byte[1024 * 1024];
                int read;

                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await call.RequestStream.WriteAsync(new SendFileRequest { Data = Google.Protobuf.ByteString.CopyFrom(buffer, 0, read) });
                }

                await call.RequestStream.CompleteAsync();
            }
            catch (Exception exception) when (exception is RpcException or InvalidOperationException)
            {
                // The server may refuse the file before all of it is written; why is in its answer below.
            }

            return await call.ResponseAsync;
        }
        catch (RpcException exception) when (exception.StatusCode != StatusCode.Cancelled)
        {
            throw Translate(exception);
        }
    }

    /// <summary>The session token is kept only in memory and sent with every call.</summary>
    private Metadata? Headers()
    {
        return _token is null ? null : new Metadata { { "authorization", $"Bearer {_token}" } };
    }

    private static async Task<T> CallAsync<T>(Func<AsyncUnaryCall<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException exception) when (exception.StatusCode != StatusCode.Cancelled)
        {
            throw Translate(exception);
        }
    }

    private static ServerClientException Translate(RpcException exception)
    {
        var message = ServerMessages.ToSlovak(exception.Status.Detail);

        return exception.StatusCode switch
        {
            StatusCode.InvalidArgument or StatusCode.FailedPrecondition => new ServerClientException(ServerErrorKind.InvalidInput, message, exception),
            StatusCode.NotFound => new ServerClientException(ServerErrorKind.NotFound, message, exception),
            StatusCode.PermissionDenied => new ServerClientException(ServerErrorKind.AccessDenied, message, exception),
            StatusCode.Unauthenticated => new ServerClientException(ServerErrorKind.NotSignedIn, message, exception),
            StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal => new ServerClientException(ServerErrorKind.Unavailable, message, exception),
            _ => new ServerClientException(ServerErrorKind.Other, message, exception),
        };
    }
}
