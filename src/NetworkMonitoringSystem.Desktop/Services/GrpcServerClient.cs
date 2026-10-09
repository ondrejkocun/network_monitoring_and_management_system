using Grpc.Core;
using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.Services;

public sealed class GrpcServerClient : IServerClient
{
    private readonly AdminApi.AdminApiClient _client;

    public GrpcServerClient(AdminApi.AdminApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(() => _client.ListDevicesAsync(new ListDevicesRequest(), cancellationToken: cancellationToken));

        return reply.Devices;
    }

    public async Task AddAgentlessDeviceAsync(string name, string ipAddress, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.AddAgentlessDeviceAsync(
            new AddAgentlessDeviceRequest { Name = name, IpAddress = ipAddress },
            cancellationToken: cancellationToken));
    }

    public Task<DeviceResourcesReply> GetDeviceResourcesAsync(string id, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetDeviceResourcesAsync(new GetDeviceResourcesRequest { Id = id }, cancellationToken: cancellationToken));
    }

    public Task<DeviceActivityReply> GetDeviceActivityAsync(string id, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetDeviceActivityAsync(new GetDeviceActivityRequest { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task RemoveDeviceAsync(string id, CancellationToken cancellationToken = default)
    {
        await CallAsync(() => _client.RemoveDeviceAsync(new RemoveDeviceRequest { Id = id }, cancellationToken: cancellationToken));
    }

    public Task<MonitoringSettingsInfo> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.GetMonitoringSettingsAsync(new GetMonitoringSettingsRequest(), cancellationToken: cancellationToken));
    }

    public Task<MonitoringSettingsInfo> UpdateSyncIntervalAsync(int seconds, CancellationToken cancellationToken = default)
    {
        return CallAsync(() => _client.UpdateSyncIntervalAsync(
            new UpdateSyncIntervalRequest { SyncIntervalSeconds = seconds },
            cancellationToken: cancellationToken));
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
                StatusCode.PermissionDenied or StatusCode.Unauthenticated => new ServerClientException(ServerErrorKind.AccessDenied, exception.Status.Detail, exception),
                StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal => new ServerClientException(ServerErrorKind.Unavailable, exception.Status.Detail, exception),
                _ => new ServerClientException(ServerErrorKind.Other, exception.Status.Detail, exception),
            };
        }
    }
}
