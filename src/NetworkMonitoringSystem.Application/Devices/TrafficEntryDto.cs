using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Devices;

/// <summary>How much a device communicated with one remote address over one protocol and service in a period.</summary>
public sealed record TrafficEntryDto(
    TrafficTransport Transport,
    string Protocol,
    string RemoteAddress,
    int? ServicePort,
    long PacketsSent,
    long PacketsReceived,
    long BytesSent,
    long BytesReceived);
