using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

public sealed class DeviceService : IDeviceService
{
    private readonly IDeviceRepository _deviceRepository;

    public DeviceService(IDeviceRepository deviceRepository)
    {
        ArgumentNullException.ThrowIfNull(deviceRepository);

        _deviceRepository = deviceRepository;
    }

    public async Task<DeviceDto> RegisterDeviceAsync(string name, string hostName, CancellationToken cancellationToken = default)
    {
        var device = new Device(Guid.NewGuid(), name, hostName);

        await _deviceRepository.AddAsync(device, cancellationToken);

        return DeviceDto.FromDevice(device);
    }

    public async Task<DeviceDto?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(id, cancellationToken);

        return device is null ? null : DeviceDto.FromDevice(device);
    }

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _deviceRepository.GetAllAsync(cancellationToken);

        return devices.Select(DeviceDto.FromDevice).ToList();
    }
}
