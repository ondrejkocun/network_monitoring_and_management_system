namespace NetworkMonitoringSystem.Agent.Identity;

/// <summary>Identity the server issued to this agent at registration.</summary>
public sealed record AgentIdentity(string DeviceId, string AgentKey);
