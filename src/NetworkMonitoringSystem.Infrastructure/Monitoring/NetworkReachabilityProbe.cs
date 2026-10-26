using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Infrastructure.Monitoring;

/// <summary>
/// Checks reachability with an ICMP echo request (ping) and, for a device that blocks ping,
/// with a connection to its TCP port.
/// </summary>
public sealed class NetworkReachabilityProbe : IDeviceReachabilityProbe
{
    public async Task<ReachabilityResult> ProbeAsync(
        IPAddress address,
        int? tcpPort,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var result = await PingAsync(address, timeout, cancellationToken);

        return result.IsReachable || tcpPort is null
            ? result
            : await ConnectToPortAsync(address, tcpPort.Value, timeout, cancellationToken);
    }

    private static async Task<ReachabilityResult> PingAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Checks the device by connecting to its TCP port. Only an accepted connection counts: a refused one may come
    /// from a firewall in front of the device as well, so it is not taken as evidence that the device is up.
    /// </summary>
    public static async Task<ReachabilityResult> ConnectToPortAsync(
        IPAddress address,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        using var client = new TcpClient(address.AddressFamily);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await client.ConnectAsync(address, port, timeoutSource.Token);

            return ReachabilityResult.Reachable((int)stopwatch.ElapsedMilliseconds, ReachabilityCheck.TcpPort);
        }
        catch (SocketException)
        {
            return ReachabilityResult.Unreachable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReachabilityResult.Unreachable;
        }
    }
}
