using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Agents;

/// <summary>
/// What an agent reported as running on its device: the complete current list of processes, listening ports
/// and connections. The values come from another computer and are not trusted.
/// </summary>
public sealed record SystemActivityReport(
    IReadOnlyList<ProcessReport> Processes,
    IReadOnlyList<ListeningPortReport> ListeningPorts,
    IReadOnlyList<ConnectionReport> Connections)
{
    public const int MaxProcesses = 4000;
    public const int MaxListeningPorts = 4000;
    public const int MaxConnections = 2000;
}

public sealed record ProcessReport(int Pid, string Name, DateTimeOffset? StartedAt, double? CpuUsagePercent, long MemoryBytes);

public sealed record ListeningPortReport(TransportProtocol Protocol, string LocalAddress, int Port, int Pid);

public sealed record ConnectionReport(
    TransportProtocol Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int Pid);
