namespace NetworkMonitoringSystem.Application.Agents;

public interface IAgentService
{
    /// <exception cref="AgentAuthenticationException">The enrollment token is not accepted.</exception>
    Task<AgentRegistration> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Records that the device is alive and, when given, the system resources its agent measured.</summary>
    /// <exception cref="AgentAuthenticationException">The credentials are not accepted.</exception>
    Task<AgentHeartbeatResult> ReportHeartbeatAsync(
        AgentCredentials credentials,
        SystemMetricsReport? metrics = null,
        CancellationToken cancellationToken = default);
}
