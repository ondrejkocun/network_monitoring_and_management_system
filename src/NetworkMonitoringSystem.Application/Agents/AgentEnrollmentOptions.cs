namespace NetworkMonitoringSystem.Application.Agents;

public sealed class AgentEnrollmentOptions
{
    public const string SectionName = "Agents";

    /// <summary>
    /// Shared secret that lets any computer register. Optional: without it every agent needs a one-time enrollment
    /// code, which is what a deployment should use. A device the server already knows can be registered again only
    /// with a code made for it, unless this token is set.
    /// </summary>
    public string? EnrollmentToken { get; set; }
}
