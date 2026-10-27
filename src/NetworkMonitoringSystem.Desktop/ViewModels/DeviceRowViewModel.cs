using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>One device as shown in the device list.</summary>
public sealed class DeviceRowViewModel
{
    private readonly HashSet<string> _permissions;

    public DeviceRowViewModel(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Id = device.Id;
        Name = device.Name;
        Address = string.IsNullOrEmpty(device.IpAddress) ? device.HostName : device.IpAddress;

        if (device.HasTcpPort)
        {
            Address += $" (TCP {device.TcpPort})";
        }
        Status = device.Status;
        HasAgent = device.MonitoringMode == MonitoringMode.Agent;
        IsEnabled = device.IsEnabled;
        CaptureTraffic = device.CaptureTraffic;
        TrafficCaptureProblem = device.TrafficCaptureProblem;
        OperatingSystem = device.OperatingSystem;
        LastSeenAt = device.LastSeenAt?.ToDateTimeOffset().ToLocalTime();
        _permissions = device.Permissions.ToHashSet(StringComparer.Ordinal);

        ModeText = device.MonitoringMode switch
        {
            MonitoringMode.Agent => "S agentom",
            MonitoringMode.Agentless => "Bez agenta",
            _ => string.Empty,
        };

        StatusText = !device.IsEnabled ? "Zakázané" : device.Status switch
        {
            DeviceStatus.Online => "Online",
            DeviceStatus.Offline => "Offline",
            _ => "Neznámy",
        };
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>IP address when the device has one, otherwise its host name; with the TCP port checked, if any.</summary>
    public string Address { get; }

    public string ModeText { get; }

    public DeviceStatus Status { get; }

    /// <summary>True when an agent runs on the device, so it can be sent commands.</summary>
    public bool HasAgent { get; }

    /// <summary>False when the device is disabled: it is not monitored and its agent is refused.</summary>
    public bool IsEnabled { get; }

    /// <summary>True while the device's agent is asked to capture network traffic.</summary>
    public bool CaptureTraffic { get; }

    /// <summary>Why the agent cannot capture although it is asked to; empty when there is no problem.</summary>
    public string TrafficCaptureProblem { get; }

    public string StatusText { get; }

    public string OperatingSystem { get; }

    /// <summary>Last contact in local time, or null when the device has never been seen.</summary>
    public DateTimeOffset? LastSeenAt { get; }

    public string LastSeenText => LastSeenAt?.ToString("dd.MM.yyyy HH:mm:ss") ?? "–";

    /// <summary>True when the signed-in user may manage this device: remove, rename, disable it or switch capture.</summary>
    public bool CanManage => Can("ManageDevices");

    /// <summary>
    /// True when the signed-in user holds the permission on this device. It only decides what is offered; the server
    /// checks every operation itself. A server that sends no permissions at all leaves everything offered.
    /// </summary>
    public bool Can(string permission) => _permissions.Count == 0 || _permissions.Contains(permission);
}
