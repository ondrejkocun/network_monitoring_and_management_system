using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

/// <param name="TcpPort">For a device without an agent: the TCP port checked when it does not answer ping.</param>
public sealed record RegisterDeviceRequest(
    string Name,
    MonitoringMode MonitoringMode,
    string? HostName = null,
    string? IpAddress = null,
    int? TcpPort = null);
