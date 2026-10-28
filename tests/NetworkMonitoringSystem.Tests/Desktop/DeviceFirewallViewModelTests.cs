namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public class DeviceFirewallViewModelTests
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();
    private readonly Confirmation _confirmation = new();

    [Fact]
    public async Task AddedRule_IsSentAsEntered_AndShownAsWaitingForTheAgent()
    {
        var firewall = new DeviceFirewallViewModel(_server, _confirmation, "pc", canManage: true)
        {
            Name = " No Telnet ",
            Ports = " 23 ",
            RemoteAddresses = "10.0.0.0/8",
        };
        firewall.Direction = firewall.Directions.Single(option => option.Value == "Outbound");

        await firewall.AddCommand.ExecuteAsync();

        var row = Assert.Single(firewall.Rules);
        Assert.Equal(("No Telnet", "Odchádzajúce", "TCP", "23", "10.0.0.0/8", "Blokovať", "Čaká na agenta"),
            (row.Name, row.DirectionText, row.ProtocolText, row.PortsText, row.AddressesText, row.ActionText, row.StateText));
        Assert.Equal(string.Empty, firewall.Name);
        Assert.Equal("Firewall (1)", firewall.Header);
    }

    [Fact]
    public async Task PortsAreSentOnlyForTcpAndUdp()
    {
        var firewall = new DeviceFirewallViewModel(_server, _confirmation, "pc", canManage: true) { Name = "No ping", Ports = "80" };
        firewall.Protocol = firewall.Protocols.Single(option => option.Value == "Icmp");

        Assert.False(firewall.ArePortsAllowed);
        await firewall.AddCommand.ExecuteAsync();

        Assert.Equal("všetky", Assert.Single(firewall.Rules).PortsText);
    }

    [Fact]
    public async Task StateReportedByTheAgent_AndTheProblemOfTheFirewall_AreShownInSlovak()
    {
        _server.FirewallRules.Add(new FirewallRuleInfo
        {
            Id = "1", DeviceId = "pc", Name = "Block all", Direction = "Outbound", Protocol = "Any", Action = "Block",
            State = "Failed", StateMessage = "The agent does not know this kind of rule.",
        });
        _server.FirewallProblem = "The firewall is switched off for the Public profile, so the rules have no effect.";
        var firewall = new DeviceFirewallViewModel(_server, _confirmation, "pc", canManage: false);

        await firewall.LoadAsync();

        var row = Assert.Single(firewall.Rules);
        Assert.True(row.IsFailed);
        Assert.StartsWith("Chyba: ", row.StateText);
        Assert.Contains("vypnutý", firewall.Problem);

        // Without the permission the tab only shows the rules.
        firewall.Name = "x";
        firewall.SelectedRule = row;
        Assert.False(firewall.AddCommand.CanExecute(null));
        Assert.False(firewall.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Rule_IsRemovedOnlyAfterConfirmation()
    {
        var firewall = new DeviceFirewallViewModel(_server, _confirmation, "pc", canManage: true) { Name = "No Telnet", Ports = "23" };
        await firewall.AddCommand.ExecuteAsync();
        firewall.SelectedRule = firewall.Rules[0];

        _confirmation.Answer = false;
        await firewall.RemoveCommand.ExecuteAsync();
        Assert.Single(firewall.Rules);

        _confirmation.Answer = true;
        await firewall.RemoveCommand.ExecuteAsync();
        Assert.Empty(firewall.Rules);
    }

    [Fact]
    public async Task RefusedRule_ShowsWhy()
    {
        var firewall = new DeviceFirewallViewModel(_server, _confirmation, "pc", canManage: true) { Name = "Bad", Ports = "99999" };
        _server.Failure = new ServerClientException(ServerErrorKind.InvalidInput, "Porty musia byť čísla od 1 do 65535.");

        await firewall.AddCommand.ExecuteAsync();

        Assert.Contains("Porty musia byť", firewall.Message);
        Assert.Equal("Bad", firewall.Name);
    }

    private sealed class Confirmation : IConfirmationDialog
    {
        public bool Answer { get; set; } = true;

        public bool Confirm(string title, string question) => Answer;
    }
}
