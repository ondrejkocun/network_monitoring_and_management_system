namespace NetworkMonitoringSystem.Application.Agents;

public sealed class AgentEnrollmentOptions
{
    public const string SectionName = "Agents";

    /// <summary>
    /// Shared secret an agent must present to register. Registration is refused while it is not set.
    /// </summary>
    public string? EnrollmentToken { get; set; }
}
