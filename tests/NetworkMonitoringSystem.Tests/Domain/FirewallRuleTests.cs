namespace NetworkMonitoringSystem.Tests.Domain;

using NetworkMonitoringSystem.Domain.Devices;

public class FirewallRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(" 443 ", "443")]
    [InlineData("80, 443,8000 - 8100", "80,443,8000-8100")]
    [InlineData("", null)]
    [InlineData("*", null)]
    public void Ports_AreWrittenInOneForm(string ports, string? normalized)
    {
        Assert.Equal(normalized, FirewallRule.NormalizePorts(ports));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("100-80")]
    [InlineData("http")]
    [InlineData("80;443")]
    public void InvalidPorts_AreRefused(string ports)
    {
        Assert.Throws<ArgumentException>(() => FirewallRule.NormalizePorts(ports));
    }

    [Fact]
    public void RemoteAddresses_AcceptAddressesAndNetworks_AndNothingElse()
    {
        Assert.Equal("10.0.0.0/8,192.0.2.7,2001:db8::/32", FirewallRule.NormalizeRemoteAddresses(" 10.0.0.0/8 , 192.0.2.7,2001:db8::/32"));
        Assert.Null(FirewallRule.NormalizeRemoteAddresses(" "));
        Assert.Throws<ArgumentException>(() => FirewallRule.NormalizeRemoteAddresses("example.com"));
        Assert.Throws<ArgumentException>(() => FirewallRule.NormalizeRemoteAddresses("10.0.0.1/8; del *"));
    }

    [Fact]
    public void PortsWithoutTcpOrUdp_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => new FirewallRule(
            Guid.NewGuid(), Guid.NewGuid(), "ping", FirewallDirection.Inbound, FirewallProtocol.Icmp, "80", null, FirewallAction.Block, Now, "admin"));
    }

    [Fact]
    public void Result_IsNewsOnlyWhenTheStateChanges()
    {
        var rule = new FirewallRule(
            Guid.NewGuid(), Guid.NewGuid(), "No Telnet", FirewallDirection.Outbound, FirewallProtocol.Tcp, "23", null, FirewallAction.Block, Now, "admin");

        Assert.Equal(FirewallRuleState.Pending, rule.State);
        Assert.True(rule.RecordResult(applied: false, "Access is denied.", Now));
        Assert.False(rule.RecordResult(applied: false, "Access is denied.", Now));
        Assert.True(rule.RecordResult(applied: true, "ignored", Now));
        Assert.Equal((FirewallRuleState.Applied, null), (rule.State, rule.StateMessage));
    }
}
