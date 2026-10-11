using System.Security.Cryptography;
using System.Text;

namespace NetworkMonitoringSystem.Application.Agents;

/// <summary>
/// Generates and verifies agent keys. A key is 256 random bits, so a plain SHA-256 hash is enough to store it safely.
/// </summary>
internal static class AgentKey
{
    private const int KeySizeBytes = 32;

    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(KeySizeBytes));

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static bool Matches(string key, string? expectedHash)
    {
        return expectedHash is not null && FixedTimeEquals(Hash(key), expectedHash);
    }

    /// <summary>True when the device exists, is enabled and the credentials carry its key.</summary>
    public static bool IsValidFor(Domain.Devices.Device? device, AgentCredentials credentials)
    {
        return device is { IsEnabled: true }
            && !string.IsNullOrEmpty(credentials.AgentKey)
            && Matches(credentials.AgentKey, device.AgentKeyHash);
    }

    /// <summary>Compares two secrets without revealing through timing where they differ.</summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }
}
