using System.Globalization;
using System.Security.Cryptography;

namespace NetworkMonitoringSystem.Application.Access;

/// <summary>
/// Turns passwords into hashes that are slow to guess: PBKDF2 with HMAC-SHA256 and a random salt for each password.
/// The hash carries its own parameters, so they can be raised later without breaking stored passwords.
/// </summary>
public static class PasswordHasher
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 256;

    private const string Algorithm = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    /// <exception cref="ArgumentException">The password is too short or too long.</exception>
    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            throw new ArgumentException(
                $"The password must have {MinPasswordLength} to {MaxPasswordLength} characters.",
                nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return string.Join(
            '$',
            Algorithm,
            Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>Returns false for a wrong password and for a stored hash that cannot be read.</summary>
    public static bool Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(storedHash);

        var parts = storedHash.Split('$');

        if (parts.Length != 4
            || parts[0] != Algorithm
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1
            || password.Length > MaxPasswordLength)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
