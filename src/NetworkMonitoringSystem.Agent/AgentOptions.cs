namespace NetworkMonitoringSystem.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Address of the server, for example https://server.example:7006.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the server's certificate. When set, the agent talks only to a server that presents exactly
    /// this certificate; when empty, the certificate must be trusted by Windows.
    /// </summary>
    public string? ServerCertificateSha256 { get; set; }

    /// <summary>
    /// True when the identity belongs to the computer and not to the user who started the agent: it is kept
    /// under ProgramData, readable only by the system and administrators. Set automatically when the agent runs
    /// as a Windows service or registers for one.
    /// </summary>
    public bool MachineWideIdentity { get; set; }

    /// <summary>Shared secret used once, to register this device with the server.</summary>
    public string? EnrollmentToken { get; set; }

    /// <summary>
    /// File holding the identity issued by the server. Defaults to a file in the user's local application data
    /// folder, or under ProgramData for a machine-wide identity.
    /// </summary>
    public string? IdentityFilePath { get; set; }

    /// <summary>How long to wait before the next attempt when the server cannot be reached or refuses the agent.</summary>
    public int RetryIntervalSeconds { get; set; } = 15;

    /// <summary>TShark used to capture traffic. Defaults to where Wireshark installs it.</summary>
    public string? TsharkPath { get; set; }

    /// <summary>
    /// Npcap interfaces to capture on, as TShark names them (tshark -D). When empty, every active network adapter
    /// of this computer is used.
    /// </summary>
    public string[]? CaptureInterfaces { get; set; }
}
