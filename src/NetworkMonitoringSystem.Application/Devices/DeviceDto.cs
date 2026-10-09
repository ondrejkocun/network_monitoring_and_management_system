using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

public sealed record DeviceDto(
    Guid Id,
    string Name,
    MonitoringMode MonitoringMode,
    string? HostName,
    string? IpAddress,
    DeviceStatus Status,
    DateTimeOffset? LastSeenAt,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    string? OperatingSystem)
{
    public static DeviceDto FromDevice(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceDto(
            device.Id,
            device.Name,
            device.MonitoringMode,
            device.HostName,
            device.IpAddress?.ToString(),
            device.Status,
            device.LastSeenAt,
            device.IsEnabled,
            device.CreatedAt,
            device.OperatingSystem);
    }
}
