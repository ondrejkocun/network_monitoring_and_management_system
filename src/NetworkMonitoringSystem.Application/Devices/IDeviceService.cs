namespace NetworkMonitoringSystem.Application.Devices;

public interface IDeviceService
{
    Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    Task<DeviceDto?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default);
}
