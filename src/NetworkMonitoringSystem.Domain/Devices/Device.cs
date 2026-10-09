using System.Net;

namespace NetworkMonitoringSystem.Domain.Devices;

public sealed class Device
{
    public Device(
        Guid id,
        string name,
        MonitoringMode monitoringMode,
        string? hostName,
        IPAddress? ipAddress,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Device name must not be empty.", nameof(name));
        }

        if (!Enum.IsDefined(monitoringMode))
        {
            throw new ArgumentOutOfRangeException(nameof(monitoringMode), monitoringMode, "Unknown monitoring mode.");
        }

        if (monitoringMode == MonitoringMode.Agent && string.IsNullOrWhiteSpace(hostName))
        {
            throw new ArgumentException("Device with an agent must have a host name.", nameof(hostName));
        }

        if (monitoringMode == MonitoringMode.Agentless && ipAddress is null)
        {
            throw new ArgumentException("Device without an agent must have an IP address.", nameof(ipAddress));
        }

        Id = id;
        Name = name.Trim();
        MonitoringMode = monitoringMode;
        HostName = string.IsNullOrWhiteSpace(hostName) ? null : hostName.Trim();
        IpAddress = ipAddress;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public string Name { get; }

    public MonitoringMode MonitoringMode { get; }

    public string? HostName { get; }

    public IPAddress? IpAddress { get; }

    public DateTimeOffset CreatedAt { get; }

    public DeviceStatus Status { get; private set; } = DeviceStatus.Unknown;

    public DateTimeOffset? LastSeenAt { get; private set; }

    public bool IsEnabled { get; private set; } = true;
}
