using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Contracts.Admin;
using DomainDeviceStatus = NetworkMonitoringSystem.Domain.Devices.DeviceStatus;
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

    public AdminApiService(IDeviceService deviceService, IMonitoringSettingsService settingsService)
    {
        _deviceService = deviceService;
        _settingsService = settingsService;
    }

    public override async Task<ListDevicesReply> ListDevices(ListDevicesRequest request, ServerCallContext context)
    {
        var devices = await _deviceService.GetDevicesAsync(context.CancellationToken);

        var reply = new ListDevicesReply();
        reply.Devices.AddRange(devices.Select(ToDeviceInfo));

        return reply;
    }

    public override async Task<DeviceInfo> AddAgentlessDevice(AddAgentlessDeviceRequest request, ServerCallContext context)
    {
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
        if (!Guid.TryParse(request.Id, out var id) || !await _deviceService.RemoveDeviceAsync(id, context.CancellationToken))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "The device does not exist."));
        }

        return new RemoveDeviceReply();
    }

    public override async Task<DeviceResourcesReply> GetDeviceResources(GetDeviceResourcesRequest request, ServerCallContext context)
    {
        var resources = Guid.TryParse(request.Id, out var id)
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

        if (!Guid.TryParse(request.Id, out var id))
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

    public override async Task<MonitoringSettingsInfo> GetMonitoringSettings(GetMonitoringSettingsRequest request, ServerCallContext context)
    {
        return ToSettingsInfo(await _settingsService.GetAsync(context.CancellationToken));
    }

    public override async Task<MonitoringSettingsInfo> UpdateSyncInterval(UpdateSyncIntervalRequest request, ServerCallContext context)
    {
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
