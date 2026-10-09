namespace NetworkMonitoringSystem.Agent.Identity;

public interface IAgentIdentityStore
{
    /// <summary>Returns the stored identity, or null when the agent has not registered yet.</summary>
    AgentIdentity? Load();

    void Save(AgentIdentity identity);

    void Clear();
}
