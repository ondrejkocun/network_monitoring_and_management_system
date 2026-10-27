using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>The state of the whole network at a glance, shown while no device is selected.</summary>
public sealed class NetworkOverviewViewModel
{
    /// <summary>How far back the warnings reach.</summary>
    public static readonly TimeSpan WarningsPeriod = TimeSpan.FromHours(24);

    public const int MaxWarnings = 10;

    /// <param name="warnings">The event log of the period, or null when the user may not read it.</param>
    public NetworkOverviewViewModel(IReadOnlyList<DeviceRowViewModel> devices, EventLogReply? warnings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var enabled = devices.Where(device => device.IsEnabled).ToList();

        OnlineCount = enabled.Count(device => device.Status == DeviceStatus.Online);
        OfflineCount = enabled.Count(device => device.Status == DeviceStatus.Offline);
        DisabledCount = devices.Count - enabled.Count;
        AgentCount = devices.Count(device => device.HasAgent);
        AgentlessCount = devices.Count - AgentCount;

        OfflineDevices = enabled
            .Where(device => device.Status == DeviceStatus.Offline)
            .OrderBy(device => device.LastSeenAt ?? DateTimeOffset.MinValue)
            .Select(device => new OfflineDeviceRow(
                device.Name,
                device.Address,
                device.LastSeenText,
                device.LastSeenAt is { } lastSeen ? DeviceHistoryViewModel.FormatDuration(now - lastSeen) : "–"))
            .ToList();

        CaptureProblems = enabled
            .Where(device => device.CaptureTraffic && !string.IsNullOrEmpty(device.TrafficCaptureProblem))
            .Select(device => new CaptureProblemRow(device.Name, device.TrafficCaptureProblem))
            .ToList();

        CanShowWarnings = warnings is not null;
        Warnings = warnings?.Events
            .Where(entry => entry.Severity is EventSeverity.Warning or EventSeverity.Error)
            .OrderByDescending(entry => entry.OccurredAt.ToDateTimeOffset())
            .Take(MaxWarnings)
            .Select(entry => new WarningRow(
                DeviceHistoryViewModel.FormatTime(entry.OccurredAt.ToDateTimeOffset()),
                EventRowViewModel.DescribeSeverity(entry.Severity),
                entry.DeviceName,
                ServerMessages.ToSlovakText(entry.Message)))
            .ToList() ?? [];
    }

    public int OnlineCount { get; }

    public int OfflineCount { get; }

    public int DisabledCount { get; }

    public int AgentCount { get; }

    public int AgentlessCount { get; }

    public IReadOnlyList<OfflineDeviceRow> OfflineDevices { get; }

    public IReadOnlyList<CaptureProblemRow> CaptureProblems { get; }

    public bool HasCaptureProblems => CaptureProblems.Count > 0;

    public bool CanShowWarnings { get; }

    public IReadOnlyList<WarningRow> Warnings { get; }

    public string OfflineHeader => OfflineDevices.Count == 0 ? "Všetky povolené zariadenia sú dostupné." : $"Nedostupné zariadenia ({OfflineDevices.Count})";

    public string WarningsHeader => !CanShowWarnings
        ? "Varovania sa zobrazia s oprávnením na záznam udalostí."
        : Warnings.Count == 0 ? "Za posledných 24 hodín žiadne varovania." : "Varovania za posledných 24 hodín";
}

public sealed record OfflineDeviceRow(string Name, string Address, string LastSeenText, string OfflineForText);

public sealed record CaptureProblemRow(string Name, string Problem);

public sealed record WarningRow(string OccurredText, string SeverityText, string DeviceName, string Message);
