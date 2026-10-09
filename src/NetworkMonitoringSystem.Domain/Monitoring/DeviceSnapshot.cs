using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>
/// The state of a device as observed at one moment. Snapshots form the device's history.
/// </summary>
public sealed class DeviceSnapshot
{
    private readonly List<DiskUsage> _disks = [];
    private readonly List<NetworkInterfaceUsage> _networkInterfaces = [];

    public DeviceSnapshot(Guid deviceId, DateTimeOffset recordedAt, DeviceStatus status, int? responseTimeMs = null)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        if (responseTimeMs is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(responseTimeMs), responseTimeMs, "Response time must not be negative.");
        }

        DeviceId = deviceId;
        RecordedAt = recordedAt;
        Status = status;
        ResponseTimeMs = responseTimeMs;
    }

    public long Id { get; private set; }

    public Guid DeviceId { get; }

    public DateTimeOffset RecordedAt { get; }

    public DeviceStatus Status { get; }

    /// <summary>Network response time, known only for devices the server checks itself.</summary>
    public int? ResponseTimeMs { get; }

    /// <summary>True when the snapshot carries system resources measured by an agent.</summary>
    public bool HasResources { get; private set; }

    public double? CpuUsagePercent { get; private set; }

    public long? MemoryTotalBytes { get; private set; }

    public long? MemoryUsedBytes { get; private set; }

    public IReadOnlyList<DiskUsage> Disks => _disks;

    public IReadOnlyList<NetworkInterfaceUsage> NetworkInterfaces => _networkInterfaces;

    /// <summary>Attaches the system resources measured at the moment of the snapshot.</summary>
    public void SetResources(ResourceUsage resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (HasResources)
        {
            throw new InvalidOperationException("The snapshot already has resources.");
        }

        HasResources = true;
        CpuUsagePercent = resources.CpuUsagePercent;
        MemoryTotalBytes = resources.MemoryTotalBytes;
        MemoryUsedBytes = resources.MemoryUsedBytes;
        _disks.AddRange(resources.Disks);
        _networkInterfaces.AddRange(resources.NetworkInterfaces);
    }
}
