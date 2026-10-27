using NetworkMonitoringSystem.Desktop.Services;
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

        var sent = history.NetworkPoints.Where(point => point.HasBytesSentPerSecond).ToList();
        var received = history.NetworkPoints.Where(point => point.HasBytesReceivedPerSecond).ToList();
        var trafficScale = NiceCeiling(sent.Select(point => point.BytesSentPerSecond)
            .Concat(received.Select(point => point.BytesReceivedPerSecond))
            .DefaultIfEmpty(0)
            .Max());

        SentPoints = BuildChart(sent, point => 100 * point.BytesSentPerSecond / trafficScale, from, to);
        ReceivedPoints = BuildChart(received, point => 100 * point.BytesReceivedPerSecond / trafficScale, from, to);
        TrafficLabels = ["0", FormatRate(trafficScale / 2), FormatRate(trafficScale)];

        var responses = history.NetworkPoints.Where(point => point.HasResponseTimeMs).ToList();
        var responseScale = NiceCeiling(responses.Select(point => point.ResponseTimeMs).DefaultIfEmpty(0).Max());

        ResponsePoints = BuildChart(responses, point => 100 * point.ResponseTimeMs / responseScale, from, to);
        ResponseLabels = ["0 ms", FormatMilliseconds(responseScale / 2), FormatMilliseconds(responseScale)];
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

    /// <summary>Data sent per second, in percent of the traffic scale; a null separates stretches without measurements.</summary>
    public IReadOnlyList<ChartPoint?> SentPoints { get; }

    /// <summary>Data received per second, in percent of the traffic scale.</summary>
    public IReadOnlyList<ChartPoint?> ReceivedPoints { get; }

    /// <summary>Labels of the traffic chart at 0, half and the whole scale.</summary>
    public IReadOnlyList<string> TrafficLabels { get; }

    /// <summary>Response time to the server's checks, in percent of its scale; breaks where the device did not answer.</summary>
    public IReadOnlyList<ChartPoint?> ResponsePoints { get; }

    public IReadOnlyList<string> ResponseLabels { get; }

    /// <summary>Why the traffic chart is empty, or empty when it has data.</summary>
    public string TrafficChartMessage => SentPoints.Count > 0 || ReceivedPoints.Count > 0
        ? string.Empty
        : "Prevádzku hlási agent; v tomto období nie sú údaje.";

    /// <summary>Why the response chart is empty, or empty when it has data.</summary>
    public string ResponseChartMessage => ResponsePoints.Count > 0
        ? string.Empty
        : "Odozvu meria server pingom len pri zariadeniach bez agenta.";

    /// <summary>Formats data per second, for example "1,5 MB/s".</summary>
    public static string FormatRate(double bytesPerSecond)
    {
        string[] units = ["B/s", "kB/s", "MB/s", "GB/s"];
        var unit = 0;

        while (bytesPerSecond >= 1000 && unit < units.Length - 1)
        {
            bytesPerSecond /= 1000;
            unit++;
        }

        return string.Create(Culture, $"{bytesPerSecond:0.#} {units[unit]}");
    }

    private static string FormatMilliseconds(double milliseconds) => string.Create(Culture, $"{milliseconds:0.#} ms");

    /// <summary>The smallest of 1, 2 and 5 times a power of ten that is not below the value, so the axis reads well.</summary>
    public static double NiceCeiling(double value)
    {
        if (value <= 0 || double.IsNaN(value))
        {
            return 1;
        }

        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));

        foreach (var step in new[] { 1, 2, 5, 10 })
        {
            if (step * power >= value)
            {
                return step * power;
            }
        }

        return 10 * power;
    }

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
        IReadOnlyList<NetworkPointInfo> points,
        Func<NetworkPointInfo, double> value,
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
        Message = ServerMessages.ToSlovakText(monitoringEvent.Message);
        TypeText = DescribeType(monitoringEvent.Type);
        SeverityText = DescribeSeverity(monitoringEvent.Severity);
    }

    /// <summary>The Slovak name of an event type; a type this version does not know is shown as the server names it.</summary>
    public static string DescribeType(string type)
    {
        return type switch
        {
            "AgentRegistered" => "Registrácia agenta",
            "DeviceWentOnline" => "Zariadenie online",
            "DeviceWentOffline" => "Zariadenie offline",
            "SettingsChanged" => "Zmena nastavení",
            "DeviceRemoved" => "Odstránenie zariadenia",
            "PortOpened" => "Nový otvorený port",
            "UserLoggedIn" => "Prihlásenie",
            "LoginFailed" => "Neúspešné prihlásenie",
            "AccessDenied" => "Zamietnutý prístup",
            "AccessChanged" => "Zmena prístupových práv",
            "RemoteCommand" => "Vzdialený príkaz",
            "DeviceDisabled" => "Zakázanie zariadenia",
            "DeviceEnabled" => "Povolenie zariadenia",
            "TrafficCaptureChanged" => "Zachytávanie komunikácie",
            "DeviceRenamed" => "Premenovanie zariadenia",
            "EnrollmentCodeCreated" => "Vytvorenie registračného kódu",
            "EnrollmentCodeRevoked" => "Zrušenie registračného kódu",
            _ => type,
        };
    }

    public static string DescribeSeverity(EventSeverity severity)
    {
        return severity switch
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
