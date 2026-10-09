using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>
/// The state of a device as observed at one moment. Snapshots form the device's history.
/// </summary>
public sealed class DeviceSnapshot
{
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
}
