using System.Globalization;
using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>A period of history the user can choose to look at.</summary>
public sealed record HistoryPeriodOption(string Label, TimeSpan Length)
{
    /// <summary>Screen readers announce an item of a list by this text.</summary>
    public override string ToString() => Label;
}

/// <summary>One value of a chart: <c>X</c> from 0 (start of the period) to 1 (its end), <c>Y</c> in percent.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>The history of one device in a period, formatted for display.</summary>
public sealed class DeviceHistoryViewModel
{
    private const string TimeFormat = "dd.MM.yyyy HH:mm:ss";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("sk-SK");

    /// <param name="previous">
    /// What is on screen now. Its lists are kept where their content has not changed, so they do not jump back
    /// to the top every time the history is reloaded.
    /// </param>
    public DeviceHistoryViewModel(DeviceHistoryReply history, DateTimeOffset from, DateTimeOffset to, DeviceHistoryViewModel? previous = null)
    {
        ArgumentNullException.ThrowIfNull(history);

        Reply = history;

        AvailabilityText = history.HasAvailabilityPercent
            ? string.Create(Culture, $"{history.AvailabilityPercent:0.##} %")
            : "–";
        DowntimeText = FormatDuration(TimeSpan.FromSeconds(history.DowntimeSeconds));
        PeriodStartText = from.ToLocalTime().ToString(TimeFormat);
        PeriodEndText = to.ToLocalTime().ToString(TimeFormat);

        // The duration of an outage in progress grows, so such a list is never reused.
        var outagesUnchanged = previous is not null
            && history.Outages.Equals(previous.Reply.Outages)
            && history.Outages.All(outage => outage.EndedAt is not null);

        Outages = outagesUnchanged
            ? previous!.Outages
            : history.Outages.Select(outage => new OutageRowViewModel(outage, to)).ToList();

        Events = previous is not null && history.Events.Equals(previous.Reply.Events)
            ? previous.Events
            : history.Events.Select(monitoringEvent => new EventRowViewModel(monitoringEvent)).ToList();

        EventsHeader = history.EventsTruncated ? $"Udalosti ({Events.Count}+)" : $"Udalosti ({Events.Count})";

        CpuPoints = BuildChart(
            history.ResourcePoints.Where(point => point.HasCpuUsagePercent).ToList(),
            point => point.CpuUsagePercent,
            from,
            to);
        MemoryPoints = BuildChart(
            history.ResourcePoints.Where(point => point.MemoryTotalBytes > 0).ToList(),
            point => 100.0 * point.MemoryUsedBytes / point.MemoryTotalBytes,
            from,
            to);
    }

    /// <summary>The message this view model was built from.</summary>
    public DeviceHistoryReply Reply { get; }

    public string AvailabilityText { get; }

    public string DowntimeText { get; }

    public string PeriodStartText { get; }

    public string PeriodEndText { get; }

    public IReadOnlyList<OutageRowViewModel> Outages { get; }

    public IReadOnlyList<EventRowViewModel> Events { get; }

    public string OutagesHeader => $"Výpadky ({Outages.Count})";

    public string EventsHeader { get; }

    /// <summary>Processor use over the period; a null separates stretches without measurements.</summary>
    public IReadOnlyList<ChartPoint?> CpuPoints { get; }

    /// <summary>Share of memory in use over the period; a null separates stretches without measurements.</summary>
    public IReadOnlyList<ChartPoint?> MemoryPoints { get; }

    public bool HasCharts => CpuPoints.Count > 0 || MemoryPoints.Count > 0;

    internal static string FormatTime(DateTimeOffset time) => time.ToLocalTime().ToString(TimeFormat);

    /// <summary>Formats a duration with its two largest units, for example "2 h 5 min".</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays} d {duration.Hours} h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{duration.Hours} h {duration.Minutes} min";
        }

        return duration.TotalMinutes >= 1
            ? $"{duration.Minutes} min {duration.Seconds} s"
            : $"{duration.Seconds} s";
    }

    private static IReadOnlyList<ChartPoint?> BuildChart(
        IReadOnlyList<ResourcePointInfo> points,
        Func<ResourcePointInfo, double> value,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var chart = new List<ChartPoint?>();
        var period = (to - from).TotalSeconds;

        if (points.Count == 0 || period <= 0)
        {
            return chart;
        }

        var times = points.Select(point => point.RecordedAt.ToDateTimeOffset()).ToList();

        // A pause much longer than the usual spacing means the device was not reporting; the line is broken there.
        var spacings = times.Zip(times.Skip(1), (earlier, later) => (later - earlier).TotalSeconds).Order().ToList();
        var longestUsualSpacing = spacings.Count < 2 ? double.MaxValue : 3 * spacings[spacings.Count / 2];

        for (var index = 0; index < points.Count; index++)
        {
            if (index > 0 && (times[index] - times[index - 1]).TotalSeconds > longestUsualSpacing)
            {
                chart.Add(null);
            }

            chart.Add(new ChartPoint(
                Math.Clamp((times[index] - from).TotalSeconds / period, 0, 1),
                Math.Clamp(value(points[index]), 0, 100)));
        }

        return chart;
    }
}

public sealed class OutageRowViewModel
{
    /// <param name="now">The moment an outage in progress is measured to.</param>
    public OutageRowViewModel(OutageInfo outage, DateTimeOffset now)
    {
        var startedAt = outage.StartedAt.ToDateTimeOffset();
        var endedAt = outage.EndedAt?.ToDateTimeOffset();

        StartedText = DeviceHistoryViewModel.FormatTime(startedAt);
        EndedText = endedAt is null ? "trvá" : DeviceHistoryViewModel.FormatTime(endedAt.Value);
        DurationText = DeviceHistoryViewModel.FormatDuration((endedAt ?? now) - startedAt);
        IsOngoing = endedAt is null;
    }

    public string StartedText { get; }

    public string EndedText { get; }

    public string DurationText { get; }

    public bool IsOngoing { get; }
}

public sealed class EventRowViewModel
{
    public EventRowViewModel(EventInfo monitoringEvent)
    {
        OccurredText = DeviceHistoryViewModel.FormatTime(monitoringEvent.OccurredAt.ToDateTimeOffset());
        Message = monitoringEvent.Message;

        TypeText = monitoringEvent.Type switch
        {
            "AgentRegistered" => "Registrácia agenta",
            "DeviceWentOnline" => "Zariadenie online",
            "DeviceWentOffline" => "Zariadenie offline",
            "SettingsChanged" => "Zmena nastavení",
            "DeviceRemoved" => "Odstránenie zariadenia",
            "PortOpened" => "Nový otvorený port",
            _ => monitoringEvent.Type,
        };

        SeverityText = monitoringEvent.Severity switch
        {
            EventSeverity.Info => "Informácia",
            EventSeverity.Warning => "Varovanie",
            EventSeverity.Error => "Chyba",
            _ => string.Empty,
        };
    }

    public string OccurredText { get; }

    public string TypeText { get; }

    public string SeverityText { get; }

    public string Message { get; }
}
