using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

public sealed record DeviceDto(Guid Id, string Name, string HostName)
{
    public static DeviceDto FromDevice(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DeviceDto(device.Id, device.Name, device.HostName);
    }
}
