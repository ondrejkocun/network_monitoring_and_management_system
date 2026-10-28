namespace NetworkMonitoringSystem.Tests.Application;

using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

/// <summary>The firewall rules travel to the agent with each report reply, and what it did with them comes back.</summary>
public class AgentFirewallTests
{
    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly InMemoryFirewallRuleRepository _rules = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly AuthenticatedUser _admin = new(Guid.NewGuid(), "admin", []);
    private readonly FirewallService _firewall;

    public AgentFirewallTests()
    {
        _firewall = new FirewallService(_rules, _devices, _history, _unitOfWork, _time);
    }

    [Fact]
    public async Task Heartbeat_GivesTheAgentTheRules_AndRecordsWhatItDid_LoggingAFailureOnce()
    {
        var agents = Agents();
        var registration = await agents.RegisterAsync(new RegisterAgentRequest("token", "PC-01", null));
        var deviceId = registration.Credentials.DeviceId;
        var rule = (await _firewall.AddRuleAsync(_admin, deviceId, Rule("No Telnet")))!;

        var reply = await agents.ReportHeartbeatAsync(registration.Credentials);
        Assert.Equal(rule.Id, Assert.Single(reply.FirewallRules!).Id);

        var failed = new FirewallReport([new FirewallRuleResult(rule.Id, false, "Access is denied.")], "The firewall is switched off for the Public profile.");
        await agents.ReportHeartbeatAsync(registration.Credentials, firewall: failed);
        await agents.ReportHeartbeatAsync(registration.Credentials, firewall: failed);

        var firewall = (await _firewall.GetAsync(deviceId))!;
        Assert.Equal((FirewallRuleState.Failed, "Access is denied."), (firewall.Rules[0].State, firewall.Rules[0].StateMessage));
        Assert.Equal("The firewall is switched off for the Public profile.", firewall.Problem);
        Assert.Single(_history.Events, monitoringEvent => monitoringEvent.Type == MonitoringEventType.FirewallRuleFailed);

        await agents.ReportHeartbeatAsync(registration.Credentials, firewall: new FirewallReport([new FirewallRuleResult(rule.Id, true, null)], null));
        firewall = (await _firewall.GetAsync(deviceId))!;
        Assert.Equal((FirewallRuleState.Applied, null, null), (firewall.Rules[0].State, firewall.Rules[0].StateMessage, firewall.Problem));
    }

    [Fact]
    public async Task RemovedRule_IsGoneFromTheNextReply()
    {
        var agents = Agents();
        var registration = await agents.RegisterAsync(new RegisterAgentRequest("token", "PC-01", null));
        var rule = (await _firewall.AddRuleAsync(_admin, registration.Credentials.DeviceId, Rule("No Telnet")))!;

        await _firewall.RemoveRuleAsync(_admin, rule.Id);

        Assert.Empty((await agents.ReportHeartbeatAsync(registration.Credentials)).FirewallRules!);
    }

    private static NewFirewallRule Rule(string name) =>
        new(name, FirewallDirection.Outbound, FirewallProtocol.Tcp, "23", null, FirewallAction.Block);

    private AgentService Agents()
    {
        var activity = new InMemoryDeviceActivityRepository(_history);

        return new AgentService(
            _devices,
            new FixedSettingsRepository(new MonitoringSettings()),
            _history,
            new AvailabilityRecorder(_history),
            new DeviceActivityRecorder(activity, _history),
            activity,
            _unitOfWork,
            Options.Create(new AgentEnrollmentOptions { EnrollmentToken = "token" }),
            new InMemoryEnrollmentCodeRepository(),
            _rules,
            _time);
    }
}
