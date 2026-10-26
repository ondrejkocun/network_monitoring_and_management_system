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
    private readonly IDeviceActivityRepository _activity;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DeviceService(
        IDeviceRepository deviceRepository,
        IMonitoringHistoryRepository history,
        IDeviceActivityRepository activity,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(deviceRepository);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _deviceRepository = deviceRepository;
        _history = history;
        _activity = activity;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ipAddress = ParseIpAddress(request.IpAddress);

        var device = new Device(
            Guid.NewGuid(),
            request.Name,
            request.MonitoringMode,
            request.HostName,
            ipAddress,
            _timeProvider.GetUtcNow());

        if (request.TcpPort is not null)
        {
            if (device.MonitoringMode != MonitoringMode.Agentless)
            {
                throw new ArgumentException("Only a device without an agent is checked on a TCP port.", nameof(request));
            }

            device.SetTcpPort(request.TcpPort);
        }

        if (device.MonitoringMode == MonitoringMode.Agentless && device.IpAddress is not null)
        {
            var existingDevice = await _deviceRepository.GetByIpAddressAsync(device.IpAddress, cancellationToken);

            if (existingDevice is not null)
            {
                throw new ArgumentException(
                    $"A device with IP address '{device.IpAddress}' already exists.",
                    nameof(request));
            }
        }

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

    public async Task<DeviceActivityDto> GetActivityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DeviceActivityDto.Create(
            await _activity.GetRunningProcessesAsync(id, cancellationToken),
            await _activity.GetLatestProcessUsagesAsync(id, cancellationToken),
            await _activity.GetOpenPortsAsync(id, cancellationToken),
            await _activity.GetConnectionsAsync(id, cancellationToken));
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

    public async Task<DeviceDto?> SetDeviceEnabledAsync(
        Guid id,
        bool isEnabled,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(id, cancellationToken);

        if (device is null)
        {
            return null;
        }

        var changed = isEnabled ? device.Enable() : device.Disable();

        if (!changed)
        {
            return DeviceDto.FromDevice(device);
        }

        var now = _timeProvider.GetUtcNow();

        // Nothing is observed while the device is disabled, so its outage cannot go on.
        if (!isEnabled && await _history.GetOngoingOutageAsync(device.Id, cancellationToken) is { } outage)
        {
            outage.End(now);
        }

        _history.AddEvent(new MonitoringEvent(
            now,
            isEnabled ? MonitoringEventType.DeviceEnabled : MonitoringEventType.DeviceDisabled,
            EventSeverity.Info,
            isEnabled ? $"Device '{device.Name}' was enabled." : $"Device '{device.Name}' was disabled.",
            device.Id,
            userId));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return DeviceDto.FromDevice(device);
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
