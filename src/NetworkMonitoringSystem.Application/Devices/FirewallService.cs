using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Devices;

public sealed record FirewallRuleDto(
    Guid Id,
    Guid DeviceId,
    string Name,
    FirewallDirection Direction,
    FirewallProtocol Protocol,
    string? Ports,
    string? RemoteAddresses,
    FirewallAction Action,
    DateTimeOffset CreatedAt,
    string CreatedByUserName,
    FirewallRuleState State,
    string? StateMessage,
    DateTimeOffset? ReportedAt)
{
    public static FirewallRuleDto From(FirewallRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new FirewallRuleDto(
            rule.Id,
            rule.DeviceId,
            rule.Name,
            rule.Direction,
            rule.Protocol,
            rule.Ports,
            rule.RemoteAddresses,
            rule.Action,
            rule.CreatedAt,
            rule.CreatedByUserName,
            rule.State,
            rule.StateMessage,
            rule.ReportedAt);
    }
}

/// <summary>A rule as the administrator enters it.</summary>
public sealed record NewFirewallRule(
    string Name,
    FirewallDirection Direction,
    FirewallProtocol Protocol,
    string? Ports,
    string? RemoteAddresses,
    FirewallAction Action);

/// <summary>The rules of a device and why the agent may not be able to keep them in effect.</summary>
public sealed record DeviceFirewallDto(IReadOnlyList<FirewallRuleDto> Rules, string? Problem);

/// <summary>The firewall rules administrators define for devices with an agent.</summary>
public interface IFirewallService
{
    /// <returns>Null when there is no such device.</returns>
    Task<DeviceFirewallDto?> GetAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <returns>Null when there is no such device.</returns>
    /// <exception cref="ArgumentException">The rule is not valid, the device has no agent or already has the most rules it may.</exception>
    Task<FirewallRuleDto?> AddRuleAsync(AuthenticatedUser actor, Guid deviceId, NewFirewallRule rule, CancellationToken cancellationToken = default);

    /// <returns>False when there is no such rule.</returns>
    Task<bool> RemoveRuleAsync(AuthenticatedUser actor, Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>The device a rule belongs to, so that the permission for it can be checked before anything else.</summary>
    Task<Guid?> GetDeviceOfRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);
}

public sealed class FirewallService : IFirewallService
{
    private readonly IFirewallRuleRepository _rules;
    private readonly IDeviceRepository _devices;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public FirewallService(
        IFirewallRuleRepository rules,
        IDeviceRepository devices,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _rules = rules;
        _devices = devices;
        _history = history;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<DeviceFirewallDto?> GetAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _devices.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return null;
        }

        var rules = await _rules.GetByDeviceAsync(deviceId, cancellationToken);

        return new DeviceFirewallDto(rules.Select(FirewallRuleDto.From).ToList(), device.FirewallProblem);
    }

    public async Task<FirewallRuleDto?> AddRuleAsync(
        AuthenticatedUser actor,
        Guid deviceId,
        NewFirewallRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(rule);

        var device = await _devices.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return null;
        }

        if (device.MonitoringMode != MonitoringMode.Agent)
        {
            throw new ArgumentException("Firewall rules can be set only on a device with an agent.", nameof(deviceId));
        }

        if ((await _rules.GetByDeviceAsync(deviceId, cancellationToken)).Count >= FirewallRule.MaxRulesPerDevice)
        {
            throw new ArgumentException($"A device can have at most {FirewallRule.MaxRulesPerDevice} firewall rules.", nameof(rule));
        }

        var now = _timeProvider.GetUtcNow();
        var created = new FirewallRule(
            Guid.NewGuid(),
            deviceId,
            rule.Name,
            rule.Direction,
            rule.Protocol,
            rule.Ports,
            rule.RemoteAddresses,
            rule.Action,
            now,
            actor.UserName);

        _rules.Add(created);
        _history.AddEvent(new MonitoringEvent(
            now,
            MonitoringEventType.FirewallRuleAdded,
            EventSeverity.Info,
            $"User '{actor.UserName}' added the firewall rule '{created.Name}' ({Describe(created)}) to device '{device.Name}'.",
            deviceId,
            actor.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return FirewallRuleDto.From(created);
    }

    public async Task<bool> RemoveRuleAsync(AuthenticatedUser actor, Guid ruleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var rule = await _rules.GetByIdAsync(ruleId, cancellationToken);

        if (rule is null)
        {
            return false;
        }

        var device = await _devices.GetByIdAsync(rule.DeviceId, cancellationToken);

        _rules.Remove(rule);
        _history.AddEvent(new MonitoringEvent(
            _timeProvider.GetUtcNow(),
            MonitoringEventType.FirewallRuleRemoved,
            EventSeverity.Info,
            $"User '{actor.UserName}' removed the firewall rule '{rule.Name}' from device '{device?.Name}'.",
            rule.DeviceId,
            actor.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<Guid?> GetDeviceOfRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        return (await _rules.GetByIdAsync(ruleId, cancellationToken))?.DeviceId;
    }

    /// <summary>The rule in a few words for the event log, for example "block outbound TCP 23 to any address".</summary>
    public static string Describe(FirewallRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var protocol = rule.Protocol == FirewallProtocol.Any ? "all protocols" : rule.Protocol.ToString().ToUpperInvariant();
        var ports = rule.Ports is null ? string.Empty : $" {rule.Ports}";
        var addresses = rule.RemoteAddresses ?? "any address";

        return $"{rule.Action.ToString().ToLowerInvariant()} {rule.Direction.ToString().ToLowerInvariant()} {protocol}{ports}, {addresses}";
    }
}
