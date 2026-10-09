using System.Net;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Devices;

public sealed class DeviceService : IDeviceService
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DeviceService(
        IDeviceRepository deviceRepository,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(deviceRepository);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _deviceRepository = deviceRepository;
        _history = history;
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

    public async Task<DeviceResourcesDto?> GetLatestResourcesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var snapshot = await _history.GetLatestResourceSnapshotAsync(id, cancellationToken);

        return snapshot is null ? null : DeviceResourcesDto.FromSnapshot(snapshot);
    }

    public async Task<bool> RemoveDeviceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(id, cancellationToken);

        if (device is null)
        {
            return false;
        }

        _deviceRepository.Remove(device);

        // The event is not linked to the device, because the device no longer exists.
        _history.AddEvent(new MonitoringEvent(
            _timeProvider.GetUtcNow(),
            MonitoringEventType.DeviceRemoved,
            EventSeverity.Info,
            $"Device '{device.Name}' was removed."));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
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
