using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkMonitoringSystem.Agent.Metrics;

/// <summary>Finds the address under which this computer is seen in its network.</summary>
public static class NetworkAddress
{
    /// <summary>
    /// Returns the IPv4 address of the main network adapter: an active one with a default gateway, which is the one
    /// traffic to other networks leaves through. Without such an adapter, the first active one with an IPv4 address.
    /// Returns an empty text when there is none.
    /// </summary>
    public static string FindPrimary()
    {
        try
        {
            return FindPrimary(NetworkInterface.GetAllNetworkInterfaces())?.ToString() ?? string.Empty;
        }
        catch (NetworkInformationException)
        {
            return string.Empty;
        }
    }

    public static IPAddress? FindPrimary(IEnumerable<NetworkInterface> adapters)
    {
        var candidates = adapters
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(adapter => adapter.GetIPProperties())
            .Select(properties => (
                Address: properties.UnicastAddresses
                    .Select(unicast => unicast.Address)
                    .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)),
                HasGateway: properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any))))
            .Where(candidate => candidate.Address is not null)
            .ToList();

        return candidates.FirstOrDefault(candidate => candidate.HasGateway).Address ?? candidates.FirstOrDefault().Address;
    }
}
