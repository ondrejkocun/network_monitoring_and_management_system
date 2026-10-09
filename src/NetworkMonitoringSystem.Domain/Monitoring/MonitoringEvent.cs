namespace NetworkMonitoringSystem.Domain.Monitoring;

public enum MonitoringEventType
{
    AgentRegistered = 0,
    DeviceWentOnline = 1,
    DeviceWentOffline = 2,
    SettingsChanged = 3,
    DeviceRemoved = 4,
}

public enum EventSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// An entry in the event log: something worth knowing that happened in the monitored system.
/// </summary>
public sealed class MonitoringEvent
{
    public MonitoringEvent(
        DateTimeOffset occurredAt,
        MonitoringEventType type,
        EventSeverity severity,
        string message,
        Guid? deviceId = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Event message must not be empty.", nameof(message));
        }

        OccurredAt = occurredAt;
        Type = type;
        Severity = severity;
        Message = message.Trim();
        DeviceId = deviceId;
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; }

    public MonitoringEventType Type { get; }

    public EventSeverity Severity { get; }

    public string Message { get; }

    /// <summary>The device the event concerns, or null for an event about the system as a whole.</summary>
    public Guid? DeviceId { get; }
}
