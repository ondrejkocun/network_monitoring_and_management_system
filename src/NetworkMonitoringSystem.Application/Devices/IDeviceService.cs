namespace NetworkMonitoringSystem.Application.Devices;

public interface IDeviceService
{
    Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    Task<DeviceDto?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the device together with its recorded snapshots and outages.</summary>
    /// <returns>False when no such device exists.</returns>
    Task<bool> RemoveDeviceAsync(Guid id, CancellationToken cancellationToken = default);
}
