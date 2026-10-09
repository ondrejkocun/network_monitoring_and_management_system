using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Devices;

/// <summary>The most recent system resources reported for a device.</summary>
public sealed record DeviceResourcesDto(
    DateTimeOffset RecordedAt,
    double? CpuUsagePercent,
    long MemoryTotalBytes,
    long MemoryUsedBytes,
    IReadOnlyList<DiskDto> Disks,
    IReadOnlyList<NetworkInterfaceDto> NetworkInterfaces)
{
    public static DeviceResourcesDto FromSnapshot(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DeviceResourcesDto(
            snapshot.RecordedAt,
            snapshot.CpuUsagePercent,
            snapshot.MemoryTotalBytes ?? 0,
            snapshot.MemoryUsedBytes ?? 0,
            snapshot.Disks
                .OrderBy(disk => disk.Name)
                .Select(disk => new DiskDto(disk.Name, disk.TotalBytes, disk.FreeBytes))
                .ToList(),
            snapshot.NetworkInterfaces
                .OrderBy(networkInterface => networkInterface.Name)
                .Select(networkInterface => new NetworkInterfaceDto(
                    networkInterface.Name,
                    networkInterface.MacAddress,
                    networkInterface.IpAddress,
                    networkInterface.IsUp,
                    networkInterface.BytesSent,
                    networkInterface.BytesReceived))
                .ToList());
    }
}

public sealed record DiskDto(string Name, long TotalBytes, long FreeBytes);

public sealed record NetworkInterfaceDto(
    string Name,
    string? MacAddress,
    string? IpAddress,
    bool IsUp,
    long BytesSent,
    long BytesReceived);
