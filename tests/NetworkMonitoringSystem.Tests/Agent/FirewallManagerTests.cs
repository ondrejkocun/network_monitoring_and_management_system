namespace NetworkMonitoringSystem.Tests.Agent;

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Firewall;
using NetworkMonitoringSystem.Contracts.Agents;

public class FirewallManagerTests
{
    private readonly FakeFirewall _firewall = new();
    private readonly FirewallManager _manager;

    public FirewallManagerTests()
    {
        _manager = new FirewallManager(_firewall, Options.Create(new AgentOptions { ServerUrl = "https://192.0.2.10:7006" }), NullLogger<FirewallManager>.Instance);
    }

    [Fact]
    public void Rules_AreAddedOnce_AndThoseNotWantedAnyMoreAreRemoved()
    {
        var telnet = Rule("1", "No Telnet", "Outbound", "Tcp", "23");
        var smb = Rule("2", "No SMB", "Inbound", "Tcp", "445");

        var report = _manager.Apply([telnet, smb]);
        _manager.Apply([telnet, smb]);

        Assert.All(report.Results, result => Assert.True(result.Applied));
        Assert.Equal(2, _firewall.Rules.Count);
        Assert.Equal(2, _firewall.Added);

        _manager.Apply([telnet]);

        var left = Assert.Single(_firewall.Rules);
        Assert.Equal("NMS: No Telnet [1]", left.Name);
        Assert.Equal((false, 6, "23", true), (left.Inbound, left.Protocol, left.Ports, left.Block));
    }

    [Fact]
    public void ChangedRule_IsReplaced_AndARuleChangedOnTheComputerIsRestored()
    {
        _manager.Apply([Rule("1", "No Telnet", "Outbound", "Tcp", "23")]);

        _manager.Apply([Rule("1", "No Telnet", "Outbound", "Tcp", "23,2323")]);
        Assert.Equal("23,2323", Assert.Single(_firewall.Rules).Ports);

        // Someone deleted it by hand.
        _firewall.Rules.Clear();
        _manager.Apply([Rule("1", "No Telnet", "Outbound", "Tcp", "23,2323")]);
        Assert.Single(_firewall.Rules);
    }

    [Fact]
    public void Icmp_GetsARuleForIPv4AndOneForIPv6()
    {
        _manager.Apply([Rule("1", "No ping", "Inbound", "Icmp", "")]);

        Assert.Equal([1, 58], _firewall.Rules.Select(rule => rule.Protocol).Order());
        Assert.All(_firewall.Rules, rule => Assert.Null(rule.Ports));
    }

    [Theory]
    [InlineData("Tcp", "", "")]
    [InlineData("Any", "", "")]
    [InlineData("Tcp", "7000-7010", "")]
    [InlineData("Tcp", "", "192.0.2.0/24")]
    public void RuleThatWouldCutTheAgentOffTheServer_IsNotApplied(string protocol, string ports, string addresses)
    {
        var report = _manager.Apply([Rule("1", "Block everything", "Outbound", protocol, ports, addresses)]);

        var result = Assert.Single(report.Results);
        Assert.False(result.Applied);
        Assert.Contains("connection to the server", result.Message);
        Assert.Empty(_firewall.Rules);
    }

    [Theory]
    [InlineData("Outbound", "Udp", "", "", "Block")]
    [InlineData("Outbound", "Tcp", "443", "", "Block")]
    [InlineData("Outbound", "Tcp", "", "10.0.0.0/8", "Block")]
    [InlineData("Inbound", "Tcp", "", "", "Block")]
    [InlineData("Outbound", "Tcp", "", "", "Allow")]
    public void RuleThatLeavesTheServerReachable_IsApplied(string direction, string protocol, string ports, string addresses, string action)
    {
        var report = _manager.Apply([Rule("1", "Rule", direction, protocol, ports, addresses, action)]);

        Assert.True(Assert.Single(report.Results).Applied);
    }

    [Theory]
    [InlineData("Udp", "", "")]
    [InlineData("Udp", "53", "")]
    [InlineData("Tcp", "53", "")]
    [InlineData("Udp", "5000-6000", "")]
    [InlineData("Udp", "", "10.0.0.0/8")]
    [InlineData("Udp", "5355", "224.0.0.252")]
    public void ServerReachedByName_KeepsItsNameLookup(string protocol, string ports, string addresses)
    {
        var report = ByName().Apply([Rule("1", "Rule", "Outbound", protocol, ports, addresses)]);

        var result = Assert.Single(report.Results);
        Assert.False(result.Applied);
        Assert.Contains("looking up the name", result.Message);
        Assert.Empty(_firewall.Rules);
    }

    [Theory]
    [InlineData("Outbound", "Udp", "123", "", "Block")]
    [InlineData("Outbound", "Udp", "", "192.0.2.0/24", "Block")]
    [InlineData("Outbound", "Icmp", "", "", "Block")]
    [InlineData("Inbound", "Udp", "", "", "Block")]
    [InlineData("Outbound", "Udp", "", "", "Allow")]
    public void ServerReachedByName_AllowsRulesThatLeaveNameLookupAlone(string direction, string protocol, string ports, string addresses, string action)
    {
        var report = ByName().Apply([Rule("1", "Rule", direction, protocol, ports, addresses, action)]);

        Assert.True(Assert.Single(report.Results).Applied);
    }

    [Fact]
    public void ServerWhoseAddressIsNotKnown_CountsAsCoveredByARuleWithAddresses()
    {
        var report = ByName().Apply([Rule("1", "Rule", "Outbound", "Tcp", "7006", "192.0.2.0/24")]);

        Assert.Contains("connection to the server", Assert.Single(report.Results).Message);
    }

    [Fact]
    public void RuleTheFirewallRefuses_IsReportedWithTheReason_AndTheProblemOfTheFirewallToo()
    {
        _firewall.Refusal = new UnauthorizedAccessException();
        _firewall.Problem = "The firewall is switched off for the Public profile.";

        var report = _manager.Apply([Rule("1", "No Telnet", "Outbound", "Tcp", "23")]);

        var result = Assert.Single(report.Results);
        Assert.False(result.Applied);
        Assert.Contains("run as a service or as an administrator", result.Message);
        Assert.Equal("The firewall is switched off for the Public profile.", report.Problem);
    }

    /// <summary>A server reached by a name that cannot be looked up here, on a computer whose DNS server is 10.0.0.1.</summary>
    private FirewallManager ByName()
    {
        return new FirewallManager(
            _firewall,
            Options.Create(new AgentOptions { ServerUrl = "https://server.invalid:7006" }),
            NullLogger<FirewallManager>.Instance,
            () => [IPAddress.Parse("10.0.0.1")]);
    }

    private static FirewallRuleSpec Rule(string id, string name, string direction, string protocol, string ports, string addresses = "", string action = "Block")
    {
        return new FirewallRuleSpec
        {
            Id = id,
            Name = name,
            Direction = direction,
            Protocol = protocol,
            Ports = ports,
            RemoteAddresses = addresses,
            Action = action,
        };
    }

    private sealed class FakeFirewall : IWindowsFirewall
    {
        public List<FirewallRuleToInstall> Rules { get; } = [];

        public int Added { get; private set; }

        public Exception? Refusal { get; set; }

        public string? Problem { get; set; }

        public IReadOnlyList<InstalledFirewallRule> GetRules(string group) =>
            Rules.Select(rule => new InstalledFirewallRule(rule.Name, rule.Description)).ToList();

        public void AddRule(string group, FirewallRuleToInstall rule)
        {
            if (Refusal is not null)
            {
                throw Refusal;
            }

            Assert.Equal(FirewallManager.Group, group);
            Rules.Add(rule);
            Added++;
        }

        public void RemoveRule(string name) => Rules.RemoveAll(rule => rule.Name == name);

        public string? GetProblem() => Problem;
    }
}
