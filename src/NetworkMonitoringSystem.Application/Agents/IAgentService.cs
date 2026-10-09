namespace NetworkMonitoringSystem.Application.Agents;

public interface IAgentService
{
    /// <exception cref="AgentAuthenticationException">The enrollment token is not accepted.</exception>
    Task<AgentRegistration> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the device is alive and, when given, the system resources its agent measured
    /// and what it found running.
    /// </summary>
    /// <exception cref="AgentAuthenticationException">The credentials are not accepted.</exception>
    Task<AgentHeartbeatResult> ReportHeartbeatAsync(
        AgentCredentials credentials,
        SystemMetricsReport? metrics = null,
        SystemActivityReport? activity = null,
        CancellationToken cancellationToken = default);
}
