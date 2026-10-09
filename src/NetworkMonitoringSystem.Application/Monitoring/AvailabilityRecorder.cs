using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>
/// Applies an observed change of availability to a device and writes the matching history:
/// a snapshot for every observation, and an outage and an event whenever the status changes.
/// </summary>
public sealed class AvailabilityRecorder
{
    private readonly IMonitoringHistoryRepository _history;

    public AvailabilityRecorder(IMonitoringHistoryRepository history)
    {
        ArgumentNullException.ThrowIfNull(history);

        _history = history;
    }

    /// <summary>Records that the device was observed to be reachable.</summary>
    /// <returns>The snapshot written for this observation.</returns>
    public async Task<DeviceSnapshot> RecordOnlineAsync(
        Device device,
        DateTimeOffset seenAt,
        int? responseTimeMs = null,
        ResourceUsage? resources = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var cameOnline = device.RecordContact(seenAt);
        var snapshot = new DeviceSnapshot(device.Id, seenAt, DeviceStatus.Online, responseTimeMs);

        if (resources is not null)
        {
            snapshot.SetResources(resources);
        }

        _history.AddSnapshot(snapshot);

        if (!cameOnline)
        {
            return snapshot;
        }

        var outage = await _history.GetOngoingOutageAsync(device.Id, cancellationToken);
        outage?.End(seenAt);

        _history.AddEvent(new MonitoringEvent(
            seenAt,
            MonitoringEventType.DeviceWentOnline,
            EventSeverity.Info,
            $"Device '{device.Name}' is online.",
            device.Id));

        return snapshot;
    }

    /// <summary>
    /// Records one failed attempt to reach the device. A single failure does not make an online device offline;
    /// that happens only after it has not been seen for the configured number of intervals.
    /// </summary>
    public void RecordUnreachable(Device device, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(device);

        _history.AddSnapshot(new DeviceSnapshot(device.Id, checkedAt, DeviceStatus.Offline));

        // A device that has never answered has no contact to wait out, so it is offline straight away.
        if (device.Status == DeviceStatus.Unknown)
        {
            RecordOffline(device, checkedAt);
        }
    }

    /// <summary>Records that the device is considered offline.</summary>
    public void RecordOffline(Device device, DateTimeOffset detectedAt)
    {
        ArgumentNullException.ThrowIfNull(device);

        // The outage is counted from the last moment the device was known to be reachable.
        var lastSeenAt = device.LastSeenAt ?? detectedAt;

        if (!device.MarkOffline())
        {
            return;
        }

        _history.AddOutage(new Outage(device.Id, lastSeenAt));
        _history.AddEvent(new MonitoringEvent(
            detectedAt,
            MonitoringEventType.DeviceWentOffline,
            EventSeverity.Warning,
            $"Device '{device.Name}' is offline.",
            device.Id));
    }
}
