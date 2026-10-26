namespace NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>How the server reached a device without an agent.</summary>
public enum ReachabilityCheck
{
    /// <summary>The device answered an ICMP echo request.</summary>
    Ping = 0,

    /// <summary>The device did not answer ping, but accepted a connection to its TCP port.</summary>
    TcpPort = 1,
}
