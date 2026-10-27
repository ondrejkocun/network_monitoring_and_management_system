using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <param name="DeviceName">The device the event concerns, or null for an event of the system as a whole.</param>
/// <param name="UserName">The user who caused the event, or null when it was not caused by a user.</param>
public sealed record EventLogEntryDto(
    DateTimeOffset OccurredAt,
    MonitoringEventType Type,
    EventSeverity Severity,
    string Message,
    string? DeviceName,
    string? UserName);

/// <param name="Truncated">True when the period had more events than are returned; the newest ones are.</param>
public sealed record EventLogDto(IReadOnlyList<EventLogEntryDto> Events, bool Truncated);

public interface IEventLogService
{
    /// <summary>
    /// Returns the events of the whole system in the period, the newest first: those of every device and those
    /// of the system itself, such as changed settings, removed devices, sign-ins and refused access.
    /// </summary>
    /// <exception cref="ArgumentException">The period ends before it starts.</exception>
    Task<EventLogDto> GetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class EventLogService : IEventLogService
{
    /// <summary>Most events returned for one period, so that a long period cannot overload the client.</summary>
    public const int MaxEvents = 500;

    private readonly IMonitoringHistoryRepository _history;

    public EventLogService(IMonitoringHistoryRepository history)
    {
        _history = history;
    }

    public async Task<EventLogDto> GetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The period must not end before it starts.", nameof(to));
        }

        // One more than shown tells whether anything was left out.
        var events = await _history.GetEventLogAsync(from, to, MaxEvents + 1, cancellationToken);

        return new EventLogDto(events.Take(MaxEvents).ToList(), events.Count > MaxEvents);
    }
}
