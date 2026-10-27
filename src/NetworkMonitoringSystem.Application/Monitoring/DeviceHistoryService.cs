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
/// <param name="NetworkPoints">Network traffic and response time over the period, the oldest first.</param>
public sealed record DeviceHistoryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    double? AvailabilityPercent,
    TimeSpan Downtime,
    IReadOnlyList<OutageDto> Outages,
    IReadOnlyList<EventDto> Events,
    bool EventsTruncated,
    IReadOnlyList<NetworkPoint> NetworkPoints);

public sealed record OutageDto(DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed record EventDto(DateTimeOffset OccurredAt, MonitoringEventType Type, EventSeverity Severity, string Message);

/// <summary>The traffic counters of one network interface as an agent reported them; they count from the start of Windows.</summary>
public sealed record InterfaceCounter(string Name, long BytesSent, long BytesReceived);

/// <summary>What one snapshot says about the network: the response time the server measured, the counters an agent reported.</summary>
public sealed record NetworkSample(DateTimeOffset RecordedAt, int? ResponseTimeMs, IReadOnlyList<InterfaceCounter> Interfaces);

/// <summary>
/// The traffic of a device in bytes per second since the previous snapshot, and the response time the server measured.
/// A value is null where it is not known: traffic for a device without an agent, the response time for one with an agent
/// or while the device did not answer.
/// </summary>
public sealed record NetworkPoint(DateTimeOffset RecordedAt, double? BytesSentPerSecond, double? BytesReceivedPerSecond, double? ResponseTimeMs);

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
    public const int MaxNetworkPoints = 120;

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
        var samples = await _history.GetNetworkSamplesAsync(deviceId, from, to, cancellationToken);

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
            Downsample(ToNetworkPoints(samples), from, to, MaxNetworkPoints));
    }

    /// <summary>
    /// Turns the counters of consecutive snapshots into traffic per second. Each interface is compared with itself, so an
    /// adapter that appears or disappears does not distort the sum; a counter that went down (Windows was restarted)
    /// is left out of that step.
    /// </summary>
    public static IReadOnlyList<NetworkPoint> ToNetworkPoints(IReadOnlyList<NetworkSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var points = new List<NetworkPoint>(samples.Count);

        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            double? sentPerSecond = null;
            double? receivedPerSecond = null;

            if (index > 0)
            {
                var previous = samples[index - 1];
                var seconds = (sample.RecordedAt - previous.RecordedAt).TotalSeconds;
                var before = previous.Interfaces
                    .GroupBy(counter => counter.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                var compared = sample.Interfaces
                    .Where(counter => before.TryGetValue(counter.Name, out var earlier)
                        && counter.BytesSent >= earlier.BytesSent
                        && counter.BytesReceived >= earlier.BytesReceived)
                    .ToList();

                if (seconds > 0 && compared.Count > 0)
                {
                    sentPerSecond = compared.Sum(counter => counter.BytesSent - before[counter.Name].BytesSent) / seconds;
                    receivedPerSecond = compared.Sum(counter => counter.BytesReceived - before[counter.Name].BytesReceived) / seconds;
                }
            }

            points.Add(new NetworkPoint(sample.RecordedAt, sentPerSecond, receivedPerSecond, sample.ResponseTimeMs));
        }

        return points;
    }

    /// <summary>
    /// Reduces the points to at most <paramref name="maxPoints"/> by splitting the period into equal parts
    /// and averaging the known values of each part.
    /// </summary>
    public static IReadOnlyList<NetworkPoint> Downsample(
        IReadOnlyList<NetworkPoint> points,
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
                var first = part.First().RecordedAt;

                return new NetworkPoint(
                    // Averaged as distances from the first point, which stay small enough to be exact.
                    first + TimeSpan.FromTicks((long)part.Average(point => (point.RecordedAt - first).Ticks)),
                    part.Average(point => point.BytesSentPerSecond),
                    part.Average(point => point.BytesReceivedPerSecond),
                    part.Average(point => point.ResponseTimeMs));
            })
            .ToList();
    }
}
