using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Devices;

/// <summary>What runs on a device according to the last report of its agent.</summary>
public sealed record DeviceActivityDto(
    IReadOnlyList<ProcessDto> Processes,
    IReadOnlyList<ListeningPortDto> ListeningPorts,
    IReadOnlyList<ConnectionDto> Connections)
{
    public static DeviceActivityDto Create(
        IReadOnlyList<ProcessRun> runningProcesses,
        IReadOnlyList<ProcessUsage> latestUsages,
        IReadOnlyList<ListeningPort> openPorts,
        IReadOnlyList<ActiveConnection> connections)
    {
        var usageByRun = latestUsages
            .GroupBy(usage => usage.ProcessRun)
            .ToDictionary(group => group.Key, group => group.First());

        return new DeviceActivityDto(
            runningProcesses
                .Select(run =>
                {
                    usageByRun.TryGetValue(run, out var usage);

                    return new ProcessDto(run.Pid, run.Name, run.StartedAt, usage is not null, usage?.CpuUsagePercent, usage?.MemoryBytes ?? 0);
                })
                // The most demanding processes first, the rest by name.
                .OrderByDescending(process => process.HasUsage)
                .ThenByDescending(process => process.CpuUsagePercent ?? 0)
                .ThenByDescending(process => process.MemoryBytes)
                .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            openPorts
                .OrderBy(port => port.Port)
                .ThenBy(port => port.Protocol)
                .ThenBy(port => port.LocalAddress, StringComparer.Ordinal)
                .Select(port => new ListeningPortDto(
                    port.Protocol,
                    port.LocalAddress,
                    port.Port,
                    port.OpenedAt,
                    port.ProcessRun?.Pid,
                    port.ProcessRun?.Name))
                .ToList(),
            connections
                // Connections whose process is not known go last.
                .OrderBy(connection => connection.ProcessRun is null)
                .ThenBy(connection => connection.ProcessRun?.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.RemoteAddress, StringComparer.Ordinal)
                .ThenBy(connection => connection.RemotePort)
                .Select(connection => new ConnectionDto(
                    connection.Protocol,
                    connection.LocalAddress,
                    connection.LocalPort,
                    connection.RemoteAddress,
                    connection.RemotePort,
                    connection.State,
                    connection.ProcessRun?.Pid,
                    connection.ProcessRun?.Name))
                .ToList());
    }
}

/// <param name="HasUsage">Usage is recorded only for the most demanding processes.</param>
public sealed record ProcessDto(int Pid, string Name, DateTimeOffset StartedAt, bool HasUsage, double? CpuUsagePercent, long MemoryBytes);

public sealed record ListeningPortDto(
    TransportProtocol Protocol,
    string LocalAddress,
    int Port,
    DateTimeOffset OpenedAt,
    int? Pid,
    string? ProcessName);

public sealed record ConnectionDto(
    TransportProtocol Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int? Pid,
    string? ProcessName);
