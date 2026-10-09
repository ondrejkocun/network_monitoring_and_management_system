namespace NetworkMonitoringSystem.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Address of the server, for example https://server.example:7006.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Shared secret used once, to register this device with the server.</summary>
    public string? EnrollmentToken { get; set; }

    /// <summary>File holding the identity issued by the server. Defaults to a file in the local application data folder.</summary>
    public string? IdentityFilePath { get; set; }

    /// <summary>How long to wait before the next attempt when the server cannot be reached or refuses the agent.</summary>
    public int RetryIntervalSeconds { get; set; } = 15;
}
