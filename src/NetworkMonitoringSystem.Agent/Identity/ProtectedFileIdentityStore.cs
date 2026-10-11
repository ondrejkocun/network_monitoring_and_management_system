using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NetworkMonitoringSystem.Agent.Identity;

/// <summary>
/// Keeps the identity in a file encrypted with Windows DPAPI.
/// <para>
/// By default the file belongs to the user who runs the agent and only that account can decrypt it.
/// A machine-wide identity is for the agent running as a Windows service: an administrator registers the agent
/// and the service, under another account, must be able to read the result. It is encrypted for the computer
/// and kept in a folder only the system and administrators can open.
/// </para>
/// </summary>
public sealed class ProtectedFileIdentityStore : IAgentIdentityStore
{
    private readonly string _filePath;
    private readonly DataProtectionScope _scope;
    private readonly bool _restrictFolder;
    private readonly ILogger<ProtectedFileIdentityStore> _logger;

    public ProtectedFileIdentityStore(IOptions<AgentOptions> options, ILogger<ProtectedFileIdentityStore> logger)
    {
        _logger = logger;

        var machineWide = options.Value.MachineWideIdentity;
        var customPath = options.Value.IdentityFilePath;

        _scope = machineWide ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
        _filePath = string.IsNullOrWhiteSpace(customPath) ? GetDefaultFilePath(machineWide) : customPath;

        // A folder chosen by the administrator keeps the permissions the administrator gave it.
        _restrictFolder = machineWide && string.IsNullOrWhiteSpace(customPath);
    }

    /// <summary>Where the identity is kept when no other place is configured.</summary>
    public static string GetDefaultFilePath(bool machineWide)
    {
        var root = Environment.GetFolderPath(machineWide
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(root, "NetworkMonitoringSystem", "Agent", "identity.bin");
    }

    /// <summary>
    /// Permissions of the folder holding a machine-wide identity: full control for the system and
    /// administrators, nothing inherited, nobody else.
    /// </summary>
    public static DirectorySecurity CreateRestrictedFolderSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var account in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(account, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return security;
    }

    public AgentIdentity? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(_filePath), optionalEntropy: null, _scope);

            return JsonSerializer.Deserialize<AgentIdentity>(json);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable file (for example one written under another account) is treated as no identity.
            _logger.LogWarning(exception, "Stored agent identity in {FilePath} could not be read.", _filePath);

            return null;
        }
    }

    public void Save(AgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var folder = Path.GetDirectoryName(_filePath)!;

        if (_restrictFolder)
        {
            var directory = new DirectoryInfo(folder);

            // Applied on every save, so a folder someone created beforehand ends up restricted as well.
            if (directory.Exists)
            {
                directory.SetAccessControl(CreateRestrictedFolderSecurity());
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
                directory.Create(CreateRestrictedFolderSecurity());
            }
        }
        else
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(identity);
        File.WriteAllBytes(_filePath, ProtectedData.Protect(json, optionalEntropy: null, _scope));
    }

    public void Clear()
    {
        File.Delete(_filePath);
    }
}
