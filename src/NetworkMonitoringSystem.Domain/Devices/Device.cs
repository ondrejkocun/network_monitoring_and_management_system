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

    /// <summary>
    /// Address of the device in the network. A device without an agent is checked at this address; for a device with
    /// an agent it is the address the agent last reported, null until it has reported one.
    /// </summary>
    public IPAddress? IpAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DeviceStatus Status { get; private set; } = DeviceStatus.Unknown;

    public DateTimeOffset? LastSeenAt { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    /// <summary>Hash of the key the agent authenticates with. The key itself is never stored.</summary>
    public string? AgentKeyHash { get; private set; }

    public string? OperatingSystem { get; private set; }

    /// <summary>
    /// TCP port the server tries when a device without an agent does not answer ping, or null to rely on ping only.
    /// </summary>
    public int? TcpPort { get; private set; }

    public const int MaxTrafficCaptureProblemLength = 500;

    /// <summary>True when the device's agent should capture network traffic and report a summary of it.</summary>
    public bool CaptureTraffic { get; private set; }

    /// <summary>Why the agent cannot capture traffic although it should, as it last reported; null when there is no problem.</summary>
    public string? TrafficCaptureProblem { get; private set; }

    /// <summary>Switches traffic capture on or off. Only an agent can capture traffic.</summary>
    /// <returns>True when this changed the setting.</returns>
    public bool SetTrafficCapture(bool capture)
    {
        if (capture && MonitoringMode != MonitoringMode.Agent)
        {
            throw new InvalidOperationException("Only a device with an agent can capture network traffic.");
        }

        if (CaptureTraffic == capture)
        {
            return false;
        }

        CaptureTraffic = capture;
        TrafficCaptureProblem = null;

        return true;
    }

    /// <summary>Records why the agent cannot capture traffic, or that it can again (null). Overlong text is shortened.</summary>
    public void RecordTrafficCaptureProblem(string? problem)
    {
        problem = string.IsNullOrWhiteSpace(problem) ? null : problem.Trim();

        TrafficCaptureProblem = problem is { Length: > MaxTrafficCaptureProblemLength }
            ? problem[..MaxTrafficCaptureProblemLength]
            : problem;
    }

    /// <summary>Sets the TCP port checked when the device does not answer ping; null removes it.</summary>
    public void SetTcpPort(int? tcpPort)
    {
        if (tcpPort is not null && MonitoringMode != MonitoringMode.Agentless)
        {
            throw new InvalidOperationException("Only a device without an agent is checked on a TCP port.");
        }

        if (tcpPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(tcpPort), tcpPort, "TCP port must be between 1 and 65535.");
        }

        TcpPort = tcpPort;
    }

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

    /// <summary>
    /// Stops monitoring the device. Its status becomes unknown, because nothing is observed while it is disabled.
    /// </summary>
    /// <returns>True when the device was enabled until now.</returns>
    public bool Disable()
    {
        if (!IsEnabled)
        {
            return false;
        }

        IsEnabled = false;
        Status = DeviceStatus.Unknown;

        return true;
    }

    /// <summary>Resumes monitoring of the device.</summary>
    /// <returns>True when the device was disabled until now.</returns>
    public bool Enable()
    {
        if (IsEnabled)
        {
            return false;
        }

        IsEnabled = true;

        return true;
    }

    /// <summary>
    /// Records the address an agent reported for its device. A device without an agent keeps the address it was added
    /// with, because that is where it is checked. Loopback and unspecified addresses say nothing about the network and
    /// are ignored.
    /// </summary>
    /// <returns>True when this changed the address.</returns>
    public bool RecordNetworkAddress(IPAddress? address)
    {
        if (MonitoringMode != MonitoringMode.Agent
            || address is null
            || IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IpAddress))
        {
            return false;
        }

        IpAddress = address;

        return true;
    }

    /// <summary>Records that the device was reachable at the given time.</summary>
    /// <returns>True when this changed the status to online.</returns>
    public bool RecordContact(DateTimeOffset seenAt)
    {
        var cameOnline = Status != DeviceStatus.Online;

        Status = DeviceStatus.Online;
        LastSeenAt = seenAt;

        return cameOnline;
    }

    /// <summary>Records that the device stopped being reachable.</summary>
    /// <returns>True when this changed the status to offline.</returns>
    public bool MarkOffline()
    {
        if (Status == DeviceStatus.Offline)
        {
            return false;
        }

        Status = DeviceStatus.Offline;

        return true;
    }
}
