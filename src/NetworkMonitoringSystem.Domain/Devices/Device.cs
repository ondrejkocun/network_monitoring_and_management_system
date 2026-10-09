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

    /// <summary>Hash of the key the agent authenticates with. The key itself is never stored.</summary>
    public string? AgentKeyHash { get; private set; }

    public string? OperatingSystem { get; private set; }

    /// <summary>
    /// Sets the identity of the agent running on this device. A previously issued key stops working.
    /// </summary>
    public void AssignAgentIdentity(string agentKeyHash, string? operatingSystem)
    {
        if (MonitoringMode != MonitoringMode.Agent)
        {
            throw new InvalidOperationException("Only a device monitored by an agent can have an agent identity.");
        }

        if (string.IsNullOrWhiteSpace(agentKeyHash))
        {
            throw new ArgumentException("Agent key hash must not be empty.", nameof(agentKeyHash));
        }

        AgentKeyHash = agentKeyHash;
        OperatingSystem = string.IsNullOrWhiteSpace(operatingSystem) ? null : operatingSystem.Trim();
    }

    /// <summary>Records that the device was reachable at the given time.</summary>
    public void RecordContact(DateTimeOffset seenAt)
    {
        Status = DeviceStatus.Online;
        LastSeenAt = seenAt;
    }
}
