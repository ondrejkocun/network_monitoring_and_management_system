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

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(() => _client.ListDevicesAsync(new ListDevicesRequest(), Headers(), cancellationToken: cancellationToken));

        return reply.Devices;
    }

    public async Task AddAgentlessDeviceAsync(string name, string ipAddress, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.AddAgentlessDeviceAsync(
            new AddAgentlessDeviceRequest { Name = name, IpAddress = ipAddress },
            Headers(), cancellationToken: cancellationToken));
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

    public async Task RemoveDeviceAsync(string id, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.RemoveDeviceAsync(new RemoveDeviceRequest { Id = id }, Headers(), cancellationToken: cancellationToken));
    }

    public Task<MonitoringSettingsInfo> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetMonitoringSettingsAsync(new GetMonitoringSettingsRequest(), Headers(), cancellationToken: cancellationToken));
    }

    public Task<MonitoringSettingsInfo> UpdateSyncIntervalAsync(int seconds, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.UpdateSyncIntervalAsync(
            new UpdateSyncIntervalRequest { SyncIntervalSeconds = seconds },
            Headers(), cancellationToken: cancellationToken));
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
            throw exception.StatusCode switch
            {
                StatusCode.InvalidArgument => new ServerClientException(ServerErrorKind.InvalidInput, exception.Status.Detail, exception),
                StatusCode.NotFound => new ServerClientException(ServerErrorKind.NotFound, exception.Status.Detail, exception),
                StatusCode.PermissionDenied => new ServerClientException(ServerErrorKind.AccessDenied, exception.Status.Detail, exception),
                StatusCode.Unauthenticated => new ServerClientException(ServerErrorKind.NotSignedIn, exception.Status.Detail, exception),
                StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal => new ServerClientException(ServerErrorKind.Unavailable, exception.Status.Detail, exception),
                _ => new ServerClientException(ServerErrorKind.Other, exception.Status.Detail, exception),
            };
        }
    }
}
