using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Firewall;

/// <summary>A rule of the agent's group as the firewall holds it.</summary>
/// <param name="Name">The name the firewall knows the rule by; unique within the group.</param>
/// <param name="Description">Carries the identifier and the fingerprint of the server's rule it was made from.</param>
public sealed record InstalledFirewallRule(string Name, string Description);

/// <summary>A rule to put into the firewall, already checked.</summary>
/// <param name="Protocol">IANA protocol number: 6 TCP, 17 UDP, 1 ICMPv4, 58 ICMPv6, 256 any.</param>
/// <param name="Ports">Local ports for inbound and remote ports for outbound rules; null for all ports.</param>
/// <param name="RemoteAddresses">Comma-separated addresses and networks; null for any address.</param>
public sealed record FirewallRuleToInstall(
    string Name,
    string Description,
    bool Inbound,
    int Protocol,
    string? Ports,
    string? RemoteAddresses,
    bool Block);

/// <summary>The firewall of this computer, limited to the one group of rules the agent manages.</summary>
public interface IWindowsFirewall
{
    IReadOnlyList<InstalledFirewallRule> GetRules(string group);

    void AddRule(string group, FirewallRuleToInstall rule);

    void RemoveRule(string name);

    /// <summary>Why rules may not be in effect, for example a profile with the firewall switched off; null when there is none.</summary>
    string? GetProblem();
}

/// <summary>
/// Keeps the firewall rules of the agent's group exactly as the server wants them: adds what is missing, removes what
/// is not wanted any more and replaces what was changed, also when someone changed the rules on the computer. A rule
/// that would block the agent's own connection to the server is never put in place.
/// </summary>
public sealed class FirewallManager
{
    /// <summary>The group of the Windows Firewall the agent's rules belong to; nothing outside it is touched.</summary>
    public const string Group = "NetworkMonitoringSystem";

    private const string DescriptionPrefix = "nms:";

    /// <summary>DNS, NetBIOS name service, mDNS and LLMNR.</summary>
    private static readonly int[] NameLookupPorts = [53, 137, 5353, 5355];

    /// <summary>Where mDNS and LLMNR ask, over IPv4 and IPv6.</summary>
    private static readonly IPAddress[] NameLookupGroups =
    [
        IPAddress.Parse("224.0.0.251"),
        IPAddress.Parse("224.0.0.252"),
        IPAddress.Parse("ff02::fb"),
        IPAddress.Parse("ff02::1:3"),
    ];

    private readonly IWindowsFirewall _firewall;
    private readonly Uri? _server;
    private readonly ILogger<FirewallManager> _logger;
    private readonly Func<IReadOnlyList<IPAddress>> _dnsServers;

    /// <param name="dnsServers">The DNS servers of this computer; those of its network adapters when not given.</param>
    public FirewallManager(
        IWindowsFirewall firewall,
        IOptions<AgentOptions> options,
        ILogger<FirewallManager> logger,
        Func<IReadOnlyList<IPAddress>>? dnsServers = null)
    {
        _dnsServers = dnsServers ?? FindDnsServers;
        _firewall = firewall;
        _server = Uri.TryCreate(options.Value.ServerUrl, UriKind.Absolute, out var server) ? server : null;
        _logger = logger;
    }

    /// <summary>Brings the firewall in line with the rules and reports what happened to each of them.</summary>
    public FirewallReport Apply(IReadOnlyList<FirewallRuleSpec> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var report = new FirewallReport();
        var wanted = new Dictionary<string, (FirewallRuleSpec Spec, IReadOnlyList<FirewallRuleToInstall> Parts)>(StringComparer.Ordinal);
        var serverAddresses = ServerAddresses();

        foreach (var spec in rules)
        {
            var refusal = Check(spec, serverAddresses, out var parts);

            if (refusal is not null)
            {
                report.Results.Add(new FirewallRuleResult { RuleId = spec.Id, Applied = false, Message = refusal });

                continue;
            }

            foreach (var part in parts)
            {
                wanted[part.Description] = (spec, parts);
            }
        }

        IReadOnlyList<InstalledFirewallRule> installed;

        try
        {
            installed = _firewall.GetRules(Group);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report.Problem = $"The firewall rules could not be read: {exception.Message}";

            return report;
        }

        // What is not wanted, or was changed in the meantime and so has another fingerprint, goes.
        foreach (var rule in installed.Where(rule => !wanted.ContainsKey(rule.Description)))
        {
            TryRemove(rule.Name);
        }

        var present = installed.Select(rule => rule.Description).ToHashSet(StringComparer.Ordinal);

        foreach (var spec in rules.Where(spec => report.Results.All(result => result.RuleId != spec.Id)))
        {
            var failure = wanted.Values
                .Where(entry => entry.Spec.Id == spec.Id)
                .SelectMany(entry => entry.Parts)
                .DistinctBy(part => part.Description)
                .Where(part => !present.Contains(part.Description))
                .Select(TryAdd)
                .FirstOrDefault(message => message is not null);

            report.Results.Add(new FirewallRuleResult { RuleId = spec.Id, Applied = failure is null, Message = failure ?? string.Empty });
        }

        try
        {
            report.Problem = _firewall.GetProblem() ?? string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report.Problem = $"The state of the firewall could not be read: {exception.Message}";
        }

        return report;
    }

    /// <summary>
    /// Turns the server's rule into the rules the firewall gets: ICMP needs one for IPv4 and one for IPv6.
    /// </summary>
    /// <returns>Why the rule is refused; null when it can be put in place.</returns>
    public string? Check(FirewallRuleSpec spec, IReadOnlyList<IPAddress> serverAddresses, out IReadOnlyList<FirewallRuleToInstall> parts)
    {
        ArgumentNullException.ThrowIfNull(spec);

        parts = [];

        var inbound = spec.Direction == "Inbound";
        var block = spec.Action == "Block";
        int[] protocols = spec.Protocol switch
        {
            "Tcp" => [6],
            "Udp" => [17],
            "Icmp" => [1, 58],
            "Any" => [256],
            _ => [],
        };

        if (protocols.Length == 0 || (!inbound && spec.Direction != "Outbound") || (!block && spec.Action != "Allow"))
        {
            return "The agent does not know this kind of rule.";
        }

        var ports = string.IsNullOrEmpty(spec.Ports) ? null : spec.Ports;
        var addresses = string.IsNullOrEmpty(spec.RemoteAddresses) ? null : spec.RemoteAddresses;

        if (!inbound && block && WouldCutOffServer(spec.Protocol, ports, addresses, serverAddresses))
        {
            return "The rule would block the agent's connection to the server, so the agent did not apply it. "
                + "Exclude the server's address or port.";
        }

        if (!inbound && block && WouldBlockNameLookup(spec.Protocol, ports, addresses))
        {
            return "The rule would block looking up the name of the server, so the agent did not apply it. "
                + "Exclude the name lookup ports (53, 137, 5353, 5355) or use the server's IP address.";
        }

        var fingerprint = Fingerprint(spec);
        var shortId = spec.Id.Length >= 8 ? spec.Id[..8] : spec.Id;

        parts = protocols
            .Select(protocol => new FirewallRuleToInstall(
                protocols.Length == 1 ? $"NMS: {spec.Name} [{shortId}]" : $"NMS: {spec.Name} [{shortId}] {(protocol == 1 ? "IPv4" : "IPv6")}",
                $"{DescriptionPrefix}{spec.Id}:{fingerprint}:{protocol}",
                inbound,
                protocol,
                ports,
                addresses,
                block))
            .ToList();

        return null;
    }

    /// <summary>
    /// True when an outbound block covers the TCP connection to the server: the protocol includes TCP, the ports
    /// include the server's port and the addresses include one of the server's. When the agent cannot tell the server's
    /// addresses just now, any rule with addresses counts as covering them: a rule wrongly refused can be corrected, an
    /// agent cut off cannot be reached any more.
    /// </summary>
    public bool WouldCutOffServer(string protocol, string? ports, string? addresses, IReadOnlyList<IPAddress> serverAddresses)
    {
        if (_server is null || protocol is not ("Tcp" or "Any"))
        {
            return false;
        }

        if (ports is not null && !CoversPort(ports, _server.Port))
        {
            return false;
        }

        if (addresses is null || serverAddresses.Count == 0)
        {
            return true;
        }

        return serverAddresses.Any(server => addresses.Split(',').Any(entry => Covers(entry, server)));
    }

    /// <summary>
    /// True when the server is reached by name and an outbound block covers how Windows looks names up: DNS, NetBIOS,
    /// mDNS or LLMNR, the last three being what a plain computer name such as ONDREJPC needs on a local network. A rule
    /// limited to addresses matters only when it includes a DNS server of this computer or a group address of
    /// these protocols; the agent's next reconnect would fail otherwise.
    /// </summary>
    public bool WouldBlockNameLookup(string protocol, string? ports, string? addresses)
    {
        if (_server is null || IPAddress.TryParse(_server.Host, out _) || protocol == "Icmp")
        {
            return false;
        }

        if (ports is not null && !NameLookupPorts.Any(port => CoversPort(ports, port)))
        {
            return false;
        }

        return addresses is null
            || _dnsServers().Concat(NameLookupGroups).Any(address => addresses.Split(',').Any(entry => Covers(entry, address)));
    }

    private static IReadOnlyList<IPAddress> FindDnsServers()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(adapter => adapter.GetIPProperties().DnsAddresses)
                .ToList();
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            return [];
        }
    }

    private IReadOnlyList<IPAddress> ServerAddresses()
    {
        if (_server is null)
        {
            return [];
        }

        if (IPAddress.TryParse(_server.Host, out var literal))
        {
            return [literal];
        }

        try
        {
            return Dns.GetHostAddresses(_server.Host);
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ArgumentException)
        {
            return [];
        }
    }

    private static bool CoversPort(string ports, int port)
    {
        foreach (var part in ports.Split(','))
        {
            var bounds = part.Split('-');

            if (int.TryParse(bounds[0], out var from) && int.TryParse(bounds[^1], out var to) && port >= from && port <= to)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Covers(string entry, IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPNetwork.TryParse(entry, out var network)
            ? network.Contains(address)
            : IPAddress.TryParse(entry, out var single) && single.Equals(address);
    }

    /// <summary>Changes whenever the rule does, so a changed rule is replaced and not kept in its old form.</summary>
    private static string Fingerprint(FirewallRuleSpec spec)
    {
        var text = string.Join('|', spec.Name, spec.Direction, spec.Protocol, spec.Ports, spec.RemoteAddresses, spec.Action);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private void TryRemove(string name)
    {
        try
        {
            _firewall.RemoveRule(name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "The firewall rule {Name} could not be removed.", name);
        }
    }

    private string? TryAdd(FirewallRuleToInstall rule)
    {
        try
        {
            _firewall.AddRule(Group, rule);

            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "The firewall rule {Name} could not be added.", rule.Name);

            return exception is UnauthorizedAccessException
                ? "The agent may not change the firewall; it has to run as a service or as an administrator."
                : exception.Message;
        }
    }
}
