using System.Net;
using System.Net.NetworkInformation;
using NetworkMonitoringSystem.Application.Monitoring;

namespace NetworkMonitoringSystem.Infrastructure.Monitoring;

/// <summary>Checks reachability with an ICMP echo request (ping).</summary>
public sealed class PingReachabilityProbe : IDeviceReachabilityProbe
{
    public async Task<ReachabilityResult> ProbeAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        using var ping = new Ping();

        try
        {
            var reply = await ping.SendPingAsync(address, timeout, cancellationToken: cancellationToken);

            return reply.Status == IPStatus.Success
                ? ReachabilityResult.Reachable((int)reply.RoundtripTime)
                : ReachabilityResult.Unreachable;
        }
        catch (PingException)
        {
            // The request could not even be sent, for example because there is no route to the address.
            return ReachabilityResult.Unreachable;
        }
    }
}
