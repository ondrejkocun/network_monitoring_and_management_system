using System.Net;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>Outcome of checking whether a device answers on the network.</summary>
public readonly record struct ReachabilityResult(bool IsReachable, int? ResponseTimeMs, ReachabilityCheck? ReachedBy)
{
    public static ReachabilityResult Unreachable { get; } = new(false, null, null);

    public static ReachabilityResult Reachable(int responseTimeMs, ReachabilityCheck reachedBy = ReachabilityCheck.Ping)
    {
        return new(true, responseTimeMs, reachedBy);
    }
}

/// <summary>
/// Checks from the server whether a device is reachable. Used for devices that cannot run an agent.
/// </summary>
public interface IDeviceReachabilityProbe
{
    /// <summary>
    /// Pings the address and, when it does not answer and a TCP port is given, tries to connect to that port.
    /// </summary>
    /// <param name="timeout">Limit for each of the two checks.</param>
    Task<ReachabilityResult> ProbeAsync(
        IPAddress address,
        int? tcpPort,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
