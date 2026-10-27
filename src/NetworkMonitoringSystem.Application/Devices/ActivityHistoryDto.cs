using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Devices;

/// <summary>What ran and which ports were open on a device during a period, the most recent first.</summary>
/// <param name="Truncated">True when the period had more process runs or port periods than are returned.</param>
public sealed record ActivityHistoryDto(
    IReadOnlyList<ProcessRunDto> Processes,
    IReadOnlyList<PortPeriodDto> Ports,
    bool Truncated);

/// <param name="EndedAt">Null while the process is still running.</param>
public sealed record ProcessRunDto(int Pid, string Name, DateTimeOffset StartedAt, DateTimeOffset? EndedAt)
{
    public static ProcessRunDto FromRun(ProcessRun run) => new(run.Pid, run.Name, run.StartedAt, run.EndedAt);
}

/// <param name="ClosedAt">Null while the port is still open.</param>
/// <param name="Process">The process that opened the port, when it is known.</param>
public sealed record PortPeriodDto(
    TransportProtocol Protocol,
    string LocalAddress,
    int Port,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    ProcessRunDto? Process)
{
    public static PortPeriodDto FromPort(ListeningPort port)
    {
        return new PortPeriodDto(
            port.Protocol,
            port.LocalAddress,
            port.Port,
            port.OpenedAt,
            port.ClosedAt,
            port.ProcessRun is { } run ? ProcessRunDto.FromRun(run) : null);
    }
}
