using System.Runtime.InteropServices;

namespace NetworkMonitoringSystem.Agent.Firewall;

/// <summary>
/// The Windows Defender Firewall through its COM interface (HNetCfg.FwPolicy2), the same one the Windows tools use.
/// Reading works for any user; adding and removing rules needs an administrator or the service account.
/// </summary>
public sealed class WindowsFirewall : IWindowsFirewall
{
    private const int DirectionInbound = 1;
    private const int DirectionOutbound = 2;
    private const int ActionBlock = 0;
    private const int ActionAllow = 1;
    private const int AllProfiles = 0x7FFFFFFF;

    private static readonly (int Profile, string Name)[] Profiles = [(1, "Domain"), (2, "Private"), (4, "Public")];

    public IReadOnlyList<InstalledFirewallRule> GetRules(string group)
    {
        var rules = new List<InstalledFirewallRule>();
        dynamic policy = CreatePolicy();

        foreach (dynamic rule in policy.Rules)
        {
            if (string.Equals((string?)rule.Grouping, group, StringComparison.Ordinal))
            {
                rules.Add(new InstalledFirewallRule((string)rule.Name, (string?)rule.Description ?? string.Empty));
            }
        }

        return rules;
    }

    public void AddRule(string group, FirewallRuleToInstall rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        dynamic created = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
        created.Name = rule.Name;
        created.Description = rule.Description;
        created.Grouping = group;
        created.Direction = rule.Inbound ? DirectionInbound : DirectionOutbound;

        // The protocol has to be set before the ports; the firewall accepts ports only for TCP and UDP.
        created.Protocol = rule.Protocol;

        if (rule.Ports is not null)
        {
            if (rule.Inbound)
            {
                created.LocalPorts = rule.Ports;
            }
            else
            {
                created.RemotePorts = rule.Ports;
            }
        }

        created.RemoteAddresses = rule.RemoteAddresses ?? "*";
        created.Action = rule.Block ? ActionBlock : ActionAllow;
        created.Profiles = AllProfiles;
        created.Enabled = true;

        dynamic policy = CreatePolicy();
        policy.Rules.Add(created);
    }

    public void RemoveRule(string name)
    {
        dynamic policy = CreatePolicy();
        policy.Rules.Remove(name);
    }

    public string? GetProblem()
    {
        var policy = CreatePolicy();
        int current = ((dynamic)policy).CurrentProfileTypes;
        var switchedOff = new List<string>();

        foreach (var (profile, name) in Profiles)
        {
            // FirewallEnabled takes the profile as a parameter, which dynamic cannot pass to a COM property.
            var enabled = (bool)policy.GetType().InvokeMember(
                "FirewallEnabled",
                System.Reflection.BindingFlags.GetProperty,
                binder: null,
                policy,
                [profile],
                System.Globalization.CultureInfo.InvariantCulture)!;

            if ((current & profile) != 0 && !enabled)
            {
                switchedOff.Add(name);
            }
        }

        return switchedOff.Count == 0
            ? null
            : $"The firewall is switched off for the {string.Join(" and ", switchedOff)} profile, so the rules have no effect.";
    }

    private static object CreatePolicy()
    {
        return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)
            ?? throw new COMException("The Windows Firewall is not available.");
    }
}
