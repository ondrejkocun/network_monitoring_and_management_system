using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Agents;

/// <summary>System resources as an agent reported them. The values come from another computer and are not trusted.</summary>
public sealed record SystemMetricsReport(
    double? CpuUsagePercent,
    long MemoryTotalBytes,
    long MemoryUsedBytes,
    IReadOnlyList<DiskReport> Disks,
    IReadOnlyList<NetworkInterfaceReport> NetworkInterfaces)
{
    /// <summary>
    /// Converts the report into valid resource usage. Implausible values are corrected instead of rejecting
    /// the whole report, so that one bad number does not cost the device its heartbeat: values are limited
    /// to their valid range, overlong texts are shortened and items beyond the limit are dropped.
    /// </summary>
    public ResourceUsage ToResourceUsage()
    {
        var memoryTotal = Math.Max(0, MemoryTotalBytes);

        return new ResourceUsage(
            CpuUsagePercent is { } cpu && double.IsFinite(cpu) ? Math.Clamp(cpu, 0, 100) : null,
            memoryTotal,
            Math.Clamp(MemoryUsedBytes, 0, memoryTotal),
            Disks
                .Where(disk => !string.IsNullOrWhiteSpace(disk.Name))
                .Take(ResourceUsage.MaxItems)
                .Select(disk =>
                {
                    var total = Math.Max(0, disk.TotalBytes);

                    return new DiskUsage(Shorten(disk.Name, ResourceUsage.MaxNameLength)!, total, Math.Clamp(disk.FreeBytes, 0, total));
                }),
            NetworkInterfaces
                .Where(networkInterface => !string.IsNullOrWhiteSpace(networkInterface.Name))
                .Take(ResourceUsage.MaxItems)
                .Select(networkInterface => new NetworkInterfaceUsage(
                    Shorten(networkInterface.Name, ResourceUsage.MaxNameLength)!,
                    Shorten(networkInterface.MacAddress, ResourceUsage.MaxAddressLength),
                    Shorten(networkInterface.IpAddress, ResourceUsage.MaxAddressLength),
                    networkInterface.IsUp,
                    Math.Max(0, networkInterface.BytesSent),
                    Math.Max(0, networkInterface.BytesReceived))));
    }

    private static string? Shorten(string? value, int maxLength)
    {
        var trimmed = value?.Trim();

        return trimmed is { Length: > 0 } && trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

public sealed record DiskReport(string Name, long TotalBytes, long FreeBytes);

public sealed record NetworkInterfaceReport(
    string Name,
    string? MacAddress,
    string? IpAddress,
    bool IsUp,
    long BytesSent,
    long BytesReceived);
