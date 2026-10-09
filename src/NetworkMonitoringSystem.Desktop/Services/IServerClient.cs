using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.Services;

/// <summary>
/// The server as the desktop application sees it. View models depend on this interface,
/// so they can be tested without a running server.
/// </summary>
public interface IServerClient
{
    /// <summary>Signs the user in; the calls that follow are made as that user.</summary>
    /// <exception cref="ServerClientException">The server could not be reached or the credentials are wrong.</exception>
    Task<LoginReply> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>Ends the session on the server and forgets it. Does not fail when the server cannot be reached.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ServerClientException">The server could not be reached or refused the call.</exception>
    Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ServerClientException">The server could not be reached or rejected the device.</exception>
    Task AddAgentlessDeviceAsync(string name, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Returns the latest system resources of the device; <c>HasData</c> is false when there are none.</summary>
    /// <exception cref="ServerClientException">The server could not be reached or refused the call.</exception>
    Task<DeviceResourcesReply> GetDeviceResourcesAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Returns the processes, open ports and connections the device's agent last reported.</summary>
    /// <exception cref="ServerClientException">The server could not be reached or refused the call.</exception>
    Task<DeviceActivityReply> GetDeviceActivityAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Returns the availability, outages, events and resource use of the device in the period.</summary>
    /// <exception cref="ServerClientException">The server could not be reached or the device does not exist.</exception>
    Task<DeviceHistoryReply> GetDeviceHistoryAsync(string id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <exception cref="ServerClientException">The server could not be reached or the device does not exist.</exception>
    Task RemoveDeviceAsync(string id, CancellationToken cancellationToken = default);

    /// <exception cref="ServerClientException">The server could not be reached or refused the call.</exception>
    Task<MonitoringSettingsInfo> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ServerClientException">The server could not be reached or rejected the interval.</exception>
    Task<MonitoringSettingsInfo> UpdateSyncIntervalAsync(int seconds, CancellationToken cancellationToken = default);
}

public enum ServerErrorKind
{
    /// <summary>The server is not running or cannot be reached.</summary>
    Unavailable,

    /// <summary>The server rejected the entered values.</summary>
    InvalidInput,

    /// <summary>The user has no permission for the operation.</summary>
    AccessDenied,

    /// <summary>The user is not signed in, the session has expired, or the credentials are wrong.</summary>
    NotSignedIn,

    /// <summary>The item the call refers to no longer exists.</summary>
    NotFound,

    Other,
}

public sealed class ServerClientException : Exception
{
    public ServerClientException(ServerErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ServerErrorKind Kind { get; }
}
