namespace NetworkMonitoringSystem.Application.Devices;

public interface IDeviceService
{
    Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    Task<DeviceDto?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent system resources reported for the device, or null when there are none.</summary>
    Task<DeviceResourcesDto?> GetLatestResourcesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns what the device's agent last reported as running: processes, open ports and connections.</summary>
    Task<DeviceActivityDto> GetActivityAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Removes the device together with its recorded snapshots and outages.</summary>
    /// <returns>False when no such device exists.</returns>
    Task<bool> RemoveDeviceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables or enables the device. A disabled device is not checked, cannot go offline and its agent is refused.
    /// </summary>
    /// <param name="userId">The user who made the change, recorded in the event.</param>
    /// <returns>The device after the change, or null when no such device exists.</returns>
    Task<DeviceDto?> SetDeviceEnabledAsync(Guid id, bool isEnabled, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>Switches capture of network traffic on or off; the agent learns it from the reply to its next report.</summary>
    /// <param name="userId">The user who made the change, recorded in the event.</param>
    /// <returns>The device after the change, or null when no such device exists.</returns>
    /// <exception cref="ArgumentException">The device has no agent, so nothing could capture its traffic.</exception>
    Task<DeviceDto?> SetTrafficCaptureAsync(Guid id, bool capture, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the process runs and port periods of the device that overlap the period, with their start, end and
    /// owning process, the most recent first.
    /// </summary>
    /// <exception cref="ArgumentException">The period ends before it starts.</exception>
    Task<ActivityHistoryDto> GetActivityHistoryAsync(Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Returns the traffic of the device in the period, summed per conversation, the largest first.</summary>
    Task<IReadOnlyList<TrafficEntryDto>> GetTrafficAsync(Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
