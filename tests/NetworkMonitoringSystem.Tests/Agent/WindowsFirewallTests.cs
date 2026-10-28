namespace NetworkMonitoringSystem.Tests.Agent;

using System.Security.Principal;
using NetworkMonitoringSystem.Agent.Firewall;

/// <summary>Works with the real Windows Firewall of this computer; changing it needs an administrator.</summary>
public class WindowsFirewallTests
{
    private const string TestGroup = "NetworkMonitoringSystem.Tests";

    private readonly WindowsFirewall _firewall = new();

    [Fact]
    public void FirewallOfThisComputer_CanBeRead()
    {
        Assert.NotNull(_firewall.GetRules(FirewallManager.Group));

        // Null on a computer with the firewall on; otherwise it names the switched-off profile.
        var problem = _firewall.GetProblem();
        Assert.True(problem is null || problem.Contains("switched off", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule_IsAddedToItsGroup_AndRemoved_WhenRunAsAdministrator()
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            // Without elevation only the refusal can be checked.
            Assert.ThrowsAny<UnauthorizedAccessException>(() => _firewall.AddRule(TestGroup, Rule()));

            return;
        }

        try
        {
            _firewall.AddRule(TestGroup, Rule());

            Assert.Equal("nms:test", Assert.Single(_firewall.GetRules(TestGroup)).Description);
        }
        finally
        {
            foreach (var rule in _firewall.GetRules(TestGroup))
            {
                _firewall.RemoveRule(rule.Name);
            }
        }

        Assert.Empty(_firewall.GetRules(TestGroup));
    }

    private static FirewallRuleToInstall Rule() =>
        new("NMS test rule " + Guid.NewGuid().ToString("N"), "nms:test", Inbound: false, Protocol: 6, Ports: "9", RemoteAddresses: "192.0.2.0/24", Block: true);
}
