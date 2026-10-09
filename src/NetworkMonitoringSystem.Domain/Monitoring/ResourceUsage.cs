namespace NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>
/// System resources of a device at one moment, as measured by its agent.
/// </summary>
public sealed class ResourceUsage
{
    public const int MaxNameLength = 200;
    public const int MaxAddressLength = 64;
    public const int MaxItems = 64;

    public ResourceUsage(
        double? cpuUsagePercent,
        long memoryTotalBytes,
        long memoryUsedBytes,
        IEnumerable<DiskUsage> disks,
        IEnumerable<NetworkInterfaceUsage> networkInterfaces)
    {
        ArgumentNullException.ThrowIfNull(disks);
        ArgumentNullException.ThrowIfNull(networkInterfaces);
        ArgumentOutOfRangeException.ThrowIfNegative(memoryTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(memoryUsedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(memoryUsedBytes, memoryTotalBytes);

        if (cpuUsagePercent is < 0 or > 100 || (cpuUsagePercent is { } cpu && double.IsNaN(cpu)))
        {
            throw new ArgumentOutOfRangeException(nameof(cpuUsagePercent), cpuUsagePercent, "CPU usage must be between 0 and 100 percent.");
        }

        CpuUsagePercent = cpuUsagePercent;
        MemoryTotalBytes = memoryTotalBytes;
        MemoryUsedBytes = memoryUsedBytes;
        Disks = disks.ToList();
        NetworkInterfaces = networkInterfaces.ToList();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Disks.Count, MaxItems, nameof(disks));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(NetworkInterfaces.Count, MaxItems, nameof(networkInterfaces));
    }

    /// <summary>Average processor load since the previous measurement, or null when it is not known yet.</summary>
    public double? CpuUsagePercent { get; }

    public long MemoryTotalBytes { get; }

    public long MemoryUsedBytes { get; }

    public IReadOnlyList<DiskUsage> Disks { get; }

    public IReadOnlyList<NetworkInterfaceUsage> NetworkInterfaces { get; }
}

public sealed class DiskUsage
{
    public DiskUsage(string name, long totalBytes, long freeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, ResourceUsage.MaxNameLength, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(freeBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(freeBytes, totalBytes);

        Name = name;
        TotalBytes = totalBytes;
        FreeBytes = freeBytes;
    }

    public long Id { get; private set; }

    public string Name { get; }

    public long TotalBytes { get; }

    public long FreeBytes { get; }
}

public sealed class NetworkInterfaceUsage
{
    public NetworkInterfaceUsage(string name, string? macAddress, string? ipAddress, bool isUp, long bytesSent, long bytesReceived)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, ResourceUsage.MaxNameLength, nameof(name));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(macAddress?.Length ?? 0, ResourceUsage.MaxAddressLength, nameof(macAddress));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ipAddress?.Length ?? 0, ResourceUsage.MaxAddressLength, nameof(ipAddress));
        ArgumentOutOfRangeException.ThrowIfNegative(bytesSent);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesReceived);

        Name = name;
        MacAddress = string.IsNullOrEmpty(macAddress) ? null : macAddress;
        IpAddress = string.IsNullOrEmpty(ipAddress) ? null : ipAddress;
        IsUp = isUp;
        BytesSent = bytesSent;
        BytesReceived = bytesReceived;
    }

    public long Id { get; private set; }

    public string Name { get; }

    public string? MacAddress { get; }

    public string? IpAddress { get; }

    public bool IsUp { get; }

    /// <summary>Bytes sent since the interface was started; the counter is cumulative.</summary>
    public long BytesSent { get; }

    /// <summary>Bytes received since the interface was started; the counter is cumulative.</summary>
    public long BytesReceived { get; }
}
