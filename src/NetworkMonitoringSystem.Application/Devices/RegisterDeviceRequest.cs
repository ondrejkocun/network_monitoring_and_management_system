using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

public sealed record RegisterDeviceRequest(
    string Name,
    MonitoringMode MonitoringMode,
    string? HostName = null,
    string? IpAddress = null);
