using System.Globalization;
using System.Net;

namespace NetworkMonitoringSystem.Domain.Devices;

public enum FirewallDirection
{
    Inbound = 0,
    Outbound = 1,
}

public enum FirewallProtocol
{
    Any = 0,
    Tcp = 1,
    Udp = 2,
    Icmp = 3,
}

public enum FirewallAction
{
    Block = 0,
    Allow = 1,
}

/// <summary>Whether the agent has put the rule in place on the device.</summary>
public enum FirewallRuleState
{
    /// <summary>The agent has not reported on the rule since it was created.</summary>
    Pending = 0,
    Applied = 1,
    Failed = 2,
}

/// <summary>
/// A rule of the Windows Firewall that the administrator wants on a device with an agent. The server keeps the rules as
/// the desired state of the device; the agent puts them in place and reports back. A rule is made of checked fields
/// only, so nothing an administrator types reaches the device as a command.
/// </summary>
public sealed class FirewallRule
{
    public const int MaxNameLength = 100;
    public const int MaxPortsLength = 200;
    public const int MaxRemoteAddressesLength = 500;
    public const int MaxStateMessageLength = 500;

    /// <summary>More rules than this on one device would be hard to keep track of; the firewall itself is not the limit.</summary>
    public const int MaxRulesPerDevice = 100;

    public FirewallRule(
        Guid id,
        Guid deviceId,
        string name,
        FirewallDirection direction,
        FirewallProtocol protocol,
        string? ports,
        string? remoteAddresses,
        FirewallAction action,
        DateTimeOffset createdAt,
        string createdByUserName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Firewall rule identifier must not be empty.", nameof(id));
        }

        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        if (!Enum.IsDefined(direction) || !Enum.IsDefined(protocol) || !Enum.IsDefined(action))
        {
            throw new ArgumentException("Unknown direction, protocol or action of a firewall rule.");
        }

        name = name?.Trim() ?? string.Empty;

        if (name.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"The name of a firewall rule must have 1 to {MaxNameLength} characters.", nameof(name));
        }

        ports = NormalizePorts(ports);

        if (ports is not null && protocol is not (FirewallProtocol.Tcp or FirewallProtocol.Udp))
        {
            throw new ArgumentException("Ports can be given only for TCP or UDP.", nameof(ports));
        }

        Id = id;
        DeviceId = deviceId;
        Name = name;
        Direction = direction;
        Protocol = protocol;
        Ports = ports;
        RemoteAddresses = NormalizeRemoteAddresses(remoteAddresses);
        Action = action;
        CreatedAt = createdAt;
        CreatedByUserName = createdByUserName;
    }

    /// <summary>For Entity Framework, which sets the properties itself.</summary>
    private FirewallRule()
    {
        Name = string.Empty;
        CreatedByUserName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid DeviceId { get; private set; }

    public string Name { get; private set; }

    public FirewallDirection Direction { get; private set; }

    public FirewallProtocol Protocol { get; private set; }

    /// <summary>
    /// The ports the rule covers, for example "80", "80,443" or "8000-8100": the local port of incoming traffic and the
    /// remote port of outgoing traffic. Null means all ports.
    /// </summary>
    public string? Ports { get; private set; }

    /// <summary>Addresses or networks on the other side, for example "10.0.0.0/8,192.0.2.7". Null means any address.</summary>
    public string? RemoteAddresses { get; private set; }

    public FirewallAction Action { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedByUserName { get; private set; }

    public FirewallRuleState State { get; private set; }

    /// <summary>Why the agent could not put the rule in place; null when it could or has not reported yet.</summary>
    public string? StateMessage { get; private set; }

    public DateTimeOffset? ReportedAt { get; private set; }

    /// <summary>Records what the agent reported about the rule.</summary>
    /// <returns>True when the state changed, so it is news.</returns>
    public bool RecordResult(bool applied, string? message, DateTimeOffset now)
    {
        var state = applied ? FirewallRuleState.Applied : FirewallRuleState.Failed;
        message = applied || string.IsNullOrWhiteSpace(message) ? null : Shorten(message.Trim(), MaxStateMessageLength);
        var changed = state != State || message != StateMessage;

        State = state;
        StateMessage = message;
        ReportedAt = now;

        return changed;
    }

    /// <summary>
    /// The ports in a canonical form: numbers and ranges separated by commas, without spaces. Null for all ports.
    /// </summary>
    /// <exception cref="ArgumentException">The text is not a list of ports from 1 to 65535.</exception>
    public static string? NormalizePorts(string? ports)
    {
        if (string.IsNullOrWhiteSpace(ports) || ports.Trim() == "*")
        {
            return null;
        }

        var parts = ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalized = new List<string>();

        foreach (var part in parts)
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);

            if (bounds.Length is < 1 or > 2 || !bounds.All(IsPort) || (bounds.Length == 2 && ParsePort(bounds[0]) > ParsePort(bounds[1])))
            {
                throw new ArgumentException("Ports must be numbers from 1 to 65535 or ranges such as 8000-8100, separated by commas.", nameof(ports));
            }

            normalized.Add(string.Join('-', bounds.Select(bound => ParsePort(bound).ToString(CultureInfo.InvariantCulture))));
        }

        var text = string.Join(',', normalized);

        return text.Length == 0
            ? null
            : text.Length <= MaxPortsLength ? text : throw new ArgumentException($"Ports must be at most {MaxPortsLength} characters long.", nameof(ports));
    }

    /// <summary>
    /// Remote addresses in a canonical form: IP addresses and networks (CIDR) separated by commas. Null for any address.
    /// </summary>
    /// <exception cref="ArgumentException">An entry is not an IP address or a network.</exception>
    public static string? NormalizeRemoteAddresses(string? addresses)
    {
        if (string.IsNullOrWhiteSpace(addresses) || addresses.Trim() == "*")
        {
            return null;
        }

        var normalized = new List<string>();

        foreach (var part in addresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(part, out var address) && !part.Contains('/'))
            {
                normalized.Add(address.ToString());
            }
            else if (IPNetwork.TryParse(part, out var network))
            {
                normalized.Add(network.ToString());
            }
            else
            {
                throw new ArgumentException($"'{part}' is not an IP address or a network such as 10.0.0.0/8.", nameof(addresses));
            }
        }

        var text = string.Join(',', normalized);

        return text.Length == 0
            ? null
            : text.Length <= MaxRemoteAddressesLength ? text : throw new ArgumentException($"Remote addresses must be at most {MaxRemoteAddressesLength} characters long.", nameof(addresses));
    }

    private static bool IsPort(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535;

    private static int ParsePort(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Shorten(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];
}
