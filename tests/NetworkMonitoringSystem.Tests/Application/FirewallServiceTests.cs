namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class FirewallServiceTests
{
    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly InMemoryFirewallRuleRepository _rules = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly AuthenticatedUser _admin = new(Guid.NewGuid(), "admin", []);
    private readonly FirewallService _service;

    public FirewallServiceTests()
    {
        _service = new FirewallService(_rules, _devices, _history, new CountingUnitOfWork(), _time);
    }

    [Fact]
    public async Task AddedRule_IsPendingUntilTheAgentReports_AndIsLoggedWithItsAuthor()
    {
        var device = AddDevice(MonitoringMode.Agent);

        var rule = await _service.AddRuleAsync(_admin, device.Id, new NewFirewallRule(" No Telnet ", FirewallDirection.Outbound, FirewallProtocol.Tcp, "23", null, FirewallAction.Block));

        Assert.Equal(("No Telnet", "23", FirewallRuleState.Pending), (rule!.Name, rule.Ports, rule.State));
        var logged = Assert.Single(_history.Events);
        Assert.Equal((MonitoringEventType.FirewallRuleAdded, _admin.Id, device.Id), (logged.Type, logged.UserId, logged.DeviceId));
        Assert.Contains("block outbound TCP 23, any address", logged.Message);
    }

    [Fact]
    public async Task Rules_ArePossibleOnlyOnADeviceWithAnAgent_AndUpToTheLimit()
    {
        var router = AddDevice(MonitoringMode.Agentless);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddRuleAsync(_admin, router.Id, Rule("x")));
        Assert.Null(await _service.AddRuleAsync(_admin, Guid.NewGuid(), Rule("x")));

        var device = AddDevice(MonitoringMode.Agent);

        for (var index = 0; index < FirewallRule.MaxRulesPerDevice; index++)
        {
            await _service.AddRuleAsync(_admin, device.Id, Rule($"rule {index}"));
        }

        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddRuleAsync(_admin, device.Id, Rule("one too many")));
    }

    [Fact]
    public async Task RemovedRule_IsGone_AndTheRemovalIsLogged()
    {
        var device = AddDevice(MonitoringMode.Agent);
        var rule = (await _service.AddRuleAsync(_admin, device.Id, Rule("No Telnet")))!;

        Assert.Equal(device.Id, await _service.GetDeviceOfRuleAsync(rule.Id));
        Assert.True(await _service.RemoveRuleAsync(_admin, rule.Id));
        Assert.False(await _service.RemoveRuleAsync(_admin, rule.Id));

        Assert.Empty((await _service.GetAsync(device.Id))!.Rules);
        Assert.Contains(_history.Events, monitoringEvent => monitoringEvent.Type == MonitoringEventType.FirewallRuleRemoved);
    }

    private static NewFirewallRule Rule(string name) =>
        new(name, FirewallDirection.Outbound, FirewallProtocol.Tcp, "23", null, FirewallAction.Block);

    private Device AddDevice(MonitoringMode mode)
    {
        var device = mode == MonitoringMode.Agent
            ? new Device(Guid.NewGuid(), "PC", mode, "pc-" + Guid.NewGuid().ToString("N"), null, _time.GetUtcNow())
            : new Device(Guid.NewGuid(), "Router", mode, null, System.Net.IPAddress.Parse("192.0.2.1"), _time.GetUtcNow());
        _devices.Add(device);

        return device;
    }
}
