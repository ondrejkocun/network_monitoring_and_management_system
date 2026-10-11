using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NetworkMonitoringSystem.Contracts.Security;

/// <summary>
/// Lets a client trust the server by one exact certificate instead of by the certificate authorities
/// installed in Windows. The pin is the SHA-256 hash of the server's certificate. It suits a closed network
/// with a self-signed server certificate: nothing has to be added to the trusted roots of every computer,
/// and a certificate issued by anyone else is refused even when Windows would trust it.
/// </summary>
public static class CertificatePin
{
    private const int HashLengthInHex = 64;

    /// <summary>
    /// Returns the pin as 64 upper-case hexadecimal digits. Spaces, colons and hyphens, which tools
    /// print between the bytes, are ignored.
    /// </summary>
    /// <exception cref="FormatException">The text is not a SHA-256 hash.</exception>
    public static string Normalize(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        var digits = new string(pin.Where(character => character is not (' ' or ':' or '-')).ToArray()).ToUpperInvariant();

        return digits.Length == HashLengthInHex && digits.All(Uri.IsHexDigit)
            ? digits
            : throw new FormatException("A certificate pin is the SHA-256 hash of the certificate: 64 hexadecimal digits.");
    }

    /// <summary>Returns the pin of the certificate.</summary>
    public static string Of(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
    }

    /// <summary>True when the certificate is the pinned one.</summary>
    /// <param name="normalizedPin">A pin returned by <see cref="Normalize"/>.</param>
    public static bool Matches(X509Certificate? certificate, string normalizedPin)
    {
        return certificate is not null
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(certificate.GetRawCertData()),
                Convert.FromHexString(normalizedPin));
    }

    /// <summary>
    /// Creates the handler for connections to the server. With a pin, the server must present exactly
    /// the pinned certificate; without one, Windows decides as usual.
    /// </summary>
    /// <exception cref="FormatException">The pin is not a SHA-256 hash.</exception>
    public static SocketsHttpHandler CreateHandler(string? pin)
    {
        var handler = new SocketsHttpHandler
        {
            // Connections are reused, but a changed address of the server is noticed within a few minutes.
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true,
        };

        if (!string.IsNullOrWhiteSpace(pin))
        {
            var normalizedPin = Normalize(pin);

            // The pinned certificate is trusted as it is: its name and issuer do not matter, because nobody
            // else can present it without the server's private key.
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => Matches(certificate, normalizedPin),
            };
        }

        return handler;
    }
}
