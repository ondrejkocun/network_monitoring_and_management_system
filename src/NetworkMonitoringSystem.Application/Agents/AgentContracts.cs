namespace NetworkMonitoringSystem.Application.Agents;

/// <param name="NetworkAddress">Address of the device in the network, as the agent reported it or the server saw it.</param>
public sealed record RegisterAgentRequest(string EnrollmentToken, string HostName, string? OperatingSystem, System.Net.IPAddress? NetworkAddress = null);

/// <summary>Identity an agent presents with every request after it has registered.</summary>
public sealed record AgentCredentials(Guid DeviceId, string AgentKey);

public sealed record AgentRegistration(AgentCredentials Credentials, int SyncIntervalSeconds);

/// <param name="CaptureTraffic">True when the agent should capture network traffic and report a summary of it.</param>
/// <param name="FirewallRules">The firewall rules the device should have; the agent removes its rules that are not among them.</param>
public sealed record AgentHeartbeatResult(int SyncIntervalSeconds, bool CaptureTraffic = false, IReadOnlyList<Devices.FirewallRuleDto>? FirewallRules = null);

/// <summary>What the agent did with one firewall rule it was given.</summary>
public sealed record FirewallRuleResult(Guid RuleId, bool Applied, string? Message);

/// <summary>What the agent reports about the firewall rules it was given in the previous reply.</summary>
/// <param name="Problem">Why the rules may not be in effect, for example a switched-off firewall; null when there is none.</param>
public sealed record FirewallReport(IReadOnlyList<FirewallRuleResult> Results, string? Problem);
