namespace NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>
/// A period during which a device was not reachable. An outage without an end is still in progress.
/// </summary>
public sealed class Outage
{
    public Outage(Guid deviceId, DateTimeOffset startedAt)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        DeviceId = deviceId;
        StartedAt = startedAt;
    }

    public long Id { get; private set; }

    public Guid DeviceId { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsOngoing => EndedAt is null;

    public void End(DateTimeOffset endedAt)
    {
        if (!IsOngoing)
        {
            throw new InvalidOperationException("The outage has already ended.");
        }

        if (endedAt < StartedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(endedAt), endedAt, "An outage cannot end before it started.");
        }

        EndedAt = endedAt;
    }
}
