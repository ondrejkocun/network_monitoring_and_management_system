using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>The history of one device in a period: how available it was, its outages, events and resources.</summary>
/// <param name="AvailabilityPercent">
/// Share of the period the device was not in an outage, or null when the device did not exist in the period.
/// </param>
/// <param name="Downtime">Total length of the outages inside the period.</param>
/// <param name="Outages">Outages that overlap the period, the most recent first.</param>
/// <param name="Events">Events of the device in the period, the most recent first.</param>
/// <param name="EventsTruncated">True when the period has more events than were returned.</param>
/// <param name="ResourcePoints">Processor and memory use over the period, the oldest first.</param>
public sealed record DeviceHistoryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    double? AvailabilityPercent,
    TimeSpan Downtime,
    IReadOnlyList<OutageDto> Outages,
    IReadOnlyList<EventDto> Events,
    bool EventsTruncated,
    IReadOnlyList<ResourcePoint> ResourcePoints);

public sealed record OutageDto(DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed record EventDto(DateTimeOffset OccurredAt, MonitoringEventType Type, EventSeverity Severity, string Message);

/// <summary>Processor and memory use of a device at one moment.</summary>
public sealed record ResourcePoint(DateTimeOffset RecordedAt, double? CpuUsagePercent, long MemoryUsedBytes, long MemoryTotalBytes);

public interface IDeviceHistoryService
{
    /// <returns>Null when no such device exists.</returns>
    /// <exception cref="ArgumentException">The period is empty or longer than <see cref="DeviceHistoryService.MaxPeriod"/>.</exception>
    Task<DeviceHistoryDto?> GetHistoryAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class DeviceHistoryService : IDeviceHistoryService
{
    public const int MaxEvents = 200;

    /// <summary>A chart does not show more detail than this, so longer histories are averaged down to it.</summary>
    public const int MaxResourcePoints = 120;

    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(366);

    private readonly IDeviceRepository _deviceRepository;
    private readonly IMonitoringHistoryRepository _history;
    private readonly TimeProvider _timeProvider;

    public DeviceHistoryService(IDeviceRepository deviceRepository, IMonitoringHistoryRepository history, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(deviceRepository);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _deviceRepository = deviceRepository;
        _history = history;
        _timeProvider = timeProvider;
    }

    public async Task<DeviceHistoryDto?> GetHistoryAsync(
        Guid deviceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();

        if (from >= to)
        {
            throw new ArgumentException("The period must end after it starts.", nameof(to));
        }

        if (to - from > MaxPeriod)
        {
            throw new ArgumentException($"The period must not be longer than {MaxPeriod.TotalDays:0} days.", nameof(from));
        }

        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return null;
        }

        var outages = await _history.GetOutagesAsync(deviceId, from, to, cancellationToken);

        // One more than the limit is asked for, to learn whether there are more.
        var events = await _history.GetEventsAsync(deviceId, from, to, MaxEvents + 1, cancellationToken);
        var points = await _history.GetResourcePointsAsync(deviceId, from, to, cancellationToken);

        // The device is judged only for the time it existed, and not for the future.
        var observedFrom = from > device.CreatedAt ? from : device.CreatedAt;
        var now = _timeProvider.GetUtcNow();
        var observedTo = to < now ? to : now;

        var downtime = TimeSpan.Zero;
        double? availabilityPercent = null;

        if (observedTo > observedFrom)
        {
            foreach (var outage in outages)
            {
                var start = outage.StartedAt > observedFrom ? outage.StartedAt : observedFrom;
                var end = outage.EndedAt is { } endedAt && endedAt < observedTo ? endedAt : observedTo;

                if (end > start)
                {
                    downtime += end - start;
                }
            }

            var observed = observedTo - observedFrom;

            // Overlapping outages should not exist, but must not push the result below zero if they do.
            downtime = downtime > observed ? observed : downtime;
            availabilityPercent = 100.0 * (1 - (downtime / observed));
        }

        return new DeviceHistoryDto(
            from,
            to,
            availabilityPercent,
            downtime,
            outages.Select(outage => new OutageDto(outage.StartedAt, outage.EndedAt)).ToList(),
            events
                .Take(MaxEvents)
                .Select(monitoringEvent => new EventDto(
                    monitoringEvent.OccurredAt,
                    monitoringEvent.Type,
                    monitoringEvent.Severity,
                    monitoringEvent.Message))
                .ToList(),
            events.Count > MaxEvents,
            Downsample(points, from, to, MaxResourcePoints));
    }

    /// <summary>
    /// Reduces the points to at most <paramref name="maxPoints"/> by splitting the period into equal parts
    /// and averaging the points of each part.
    /// </summary>
    public static IReadOnlyList<ResourcePoint> Downsample(
        IReadOnlyList<ResourcePoint> points,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxPoints)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoints, 1);

        if (points.Count <= maxPoints)
        {
            return points;
        }

        var partTicks = Math.Max(1, (to - from).Ticks / maxPoints);

        return points
            .GroupBy(point => Math.Clamp((point.RecordedAt - from).Ticks / partTicks, 0, maxPoints - 1))
            .OrderBy(part => part.Key)
            .Select(part =>
            {
                var measuredCpu = part.Where(point => point.CpuUsagePercent is not null).ToList();
                var first = part.First().RecordedAt;

                return new ResourcePoint(
                    // Averaged as distances from the first point, which stay small enough to be exact.
                    first + TimeSpan.FromTicks((long)part.Average(point => (point.RecordedAt - first).Ticks)),
                    measuredCpu.Count == 0 ? null : measuredCpu.Average(point => point.CpuUsagePercent!.Value),
                    (long)part.Average(point => point.MemoryUsedBytes),
                    part.Max(point => point.MemoryTotalBytes));
            })
            .ToList();
    }
}
