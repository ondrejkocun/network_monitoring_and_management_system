using System.Net;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>Outcome of checking whether a device answers on the network.</summary>
public readonly record struct ReachabilityResult(bool IsReachable, int? ResponseTimeMs)
{
    public static ReachabilityResult Unreachable { get; } = new(false, null);

    public static ReachabilityResult Reachable(int responseTimeMs) => new(true, responseTimeMs);
}

/// <summary>
/// Checks from the server whether a device is reachable. Used for devices that cannot run an agent.
/// </summary>
public interface IDeviceReachabilityProbe
{
    Task<ReachabilityResult> ProbeAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken = default);
}
