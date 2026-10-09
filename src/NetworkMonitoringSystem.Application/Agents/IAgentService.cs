namespace NetworkMonitoringSystem.Application.Agents;

public interface IAgentService
{
    /// <exception cref="AgentAuthenticationException">The enrollment token is not accepted.</exception>
    Task<AgentRegistration> RegisterAsync(RegisterAgentRequest request, CancellationToken cancellationToken = default);

    /// <exception cref="AgentAuthenticationException">The credentials are not accepted.</exception>
    Task<AgentHeartbeatResult> ReportHeartbeatAsync(AgentCredentials credentials, CancellationToken cancellationToken = default);
}
