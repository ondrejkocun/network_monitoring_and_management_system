using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NetworkMonitoringSystem.Agent.Identity;

/// <summary>
/// Keeps the identity in a file encrypted with Windows DPAPI, so that only the account the agent runs under can read it.
/// </summary>
public sealed class ProtectedFileIdentityStore : IAgentIdentityStore
{
    private readonly string _filePath;
    private readonly ILogger<ProtectedFileIdentityStore> _logger;

    public ProtectedFileIdentityStore(IOptions<AgentOptions> options, ILogger<ProtectedFileIdentityStore> logger)
    {
        _logger = logger;
        _filePath = string.IsNullOrWhiteSpace(options.Value.IdentityFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetworkMonitoringSystem",
                "Agent",
                "identity.bin")
            : options.Value.IdentityFilePath;
    }

    public AgentIdentity? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(_filePath), optionalEntropy: null, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<AgentIdentity>(json);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
        {
            // An unreadable file (for example one written under another account) is treated as no identity.
            _logger.LogWarning(exception, "Stored agent identity in {FilePath} could not be read.", _filePath);

            return null;
        }
    }

    public void Save(AgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        var json = JsonSerializer.SerializeToUtf8Bytes(identity);
        File.WriteAllBytes(_filePath, ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        File.Delete(_filePath);
    }
}
