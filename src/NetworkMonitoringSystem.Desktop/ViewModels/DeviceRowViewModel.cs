using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>One device as shown in the device list.</summary>
public sealed class DeviceRowViewModel
{
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
        OperatingSystem = device.OperatingSystem;
        LastSeenAt = device.LastSeenAt?.ToDateTimeOffset().ToLocalTime();

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

    public string StatusText { get; }

    public string OperatingSystem { get; }

    /// <summary>Last contact in local time, or null when the device has never been seen.</summary>
    public DateTimeOffset? LastSeenAt { get; }

    public string LastSeenText => LastSeenAt?.ToString("dd.MM.yyyy HH:mm:ss") ?? "–";
}
