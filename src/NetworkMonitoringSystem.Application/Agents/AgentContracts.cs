namespace NetworkMonitoringSystem.Application.Agents;

/// <param name="NetworkAddress">Address of the device in the network, as the agent reported it or the server saw it.</param>
public sealed record RegisterAgentRequest(string EnrollmentToken, string HostName, string? OperatingSystem, System.Net.IPAddress? NetworkAddress = null);

/// <summary>Identity an agent presents with every request after it has registered.</summary>
public sealed record AgentCredentials(Guid DeviceId, string AgentKey);

public sealed record AgentRegistration(AgentCredentials Credentials, int SyncIntervalSeconds);

/// <param name="CaptureTraffic">True when the agent should capture network traffic and report a summary of it.</param>
public sealed record AgentHeartbeatResult(int SyncIntervalSeconds, bool CaptureTraffic = false);
