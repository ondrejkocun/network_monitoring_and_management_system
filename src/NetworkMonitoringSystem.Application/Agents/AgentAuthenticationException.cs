namespace NetworkMonitoringSystem.Application.Agents;

/// <summary>
/// Thrown when an agent presents an enrollment token or credentials the server does not accept.
/// </summary>
public sealed class AgentAuthenticationException : Exception
{
    public AgentAuthenticationException(string message)
        : base(message)
    {
    }
}
