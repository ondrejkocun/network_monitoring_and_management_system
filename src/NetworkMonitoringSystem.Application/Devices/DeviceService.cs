using System.Net;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

public sealed class DeviceService : IDeviceService
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DeviceService(IDeviceRepository deviceRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(deviceRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var device = new Device(
            Guid.NewGuid(),
            request.Name,
            request.MonitoringMode,
            request.HostName,
            ParseIpAddress(request.IpAddress),
            _timeProvider.GetUtcNow());

        _deviceRepository.Add(device);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

    private static IPAddress? ParseIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        if (!IPAddress.TryParse(ipAddress.Trim(), out var parsed))
        {
            throw new ArgumentException($"'{ipAddress}' is not a valid IP address.", nameof(ipAddress));
        }

        return parsed;
    }
}
