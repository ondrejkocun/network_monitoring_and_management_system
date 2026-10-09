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

    public async Task RecordOnlineAsync(Device device, DateTimeOffset seenAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var cameOnline = device.RecordContact(seenAt);

        _history.AddSnapshot(new DeviceSnapshot(device.Id, seenAt, DeviceStatus.Online));

        if (!cameOnline)
        {
            return;
        }

        var outage = await _history.GetOngoingOutageAsync(device.Id, cancellationToken);
        outage?.End(seenAt);

        _history.AddEvent(new MonitoringEvent(
            seenAt,
            MonitoringEventType.DeviceWentOnline,
            EventSeverity.Info,
            $"Device '{device.Name}' is online.",
            device.Id));
    }

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
