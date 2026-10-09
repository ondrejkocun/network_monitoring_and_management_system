using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

/// <summary>
/// Compares what an agent reports as running with what the server has recorded for the device and stores
/// only the differences: processes and ports are kept as periods, connections as the current state.
/// </summary>
public sealed class DeviceActivityRecorder
{
    /// <summary>Start of the range from which Windows assigns ports to outgoing communication.</summary>
    public const int FirstDynamicPort = 49152;

    private readonly IDeviceActivityRepository _activity;
    private readonly IMonitoringHistoryRepository _history;

    public DeviceActivityRecorder(IDeviceActivityRepository activity, IMonitoringHistoryRepository history)
    {
        _activity = activity;
        _history = history;
    }

    /// <summary>Applies the report to the stored state and attaches process usage to the snapshot.</summary>
    public async Task RecordAsync(
        Device device,
        SystemActivityReport report,
        DeviceSnapshot snapshot,
        int topProcessCount,
        DateTimeOffset reportedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(snapshot);

        var runningBefore = await _activity.GetRunningProcessesAsync(device.Id, cancellationToken);
        var openBefore = await _activity.GetOpenPortsAsync(device.Id, cancellationToken);

        // The first report of a device only establishes what is there; nothing in it is news.
        var isFirstReport = runningBefore.Count == 0 && openBefore.Count == 0;

        var runsByPid = ReconcileProcesses(device, report, runningBefore, reportedAt);

        ReconcilePorts(device, report, openBefore, runsByPid, reportedAt, writeEvents: !isFirstReport);
        AttachProcessUsage(report, snapshot, runsByPid, topProcessCount);

        await _activity.ReplaceConnectionsAsync(device.Id, CreateConnections(device, report, runsByPid), cancellationToken);
    }

    private Dictionary<int, ProcessRun> ReconcileProcesses(
        Device device,
        SystemActivityReport report,
        IReadOnlyList<ProcessRun> runningBefore,
        DateTimeOffset reportedAt)
    {
        var runsByPid = new Dictionary<int, ProcessRun>();
        var stillRunning = new HashSet<ProcessRun>();
        var beforeByPid = runningBefore.ToLookup(run => run.Pid);

        foreach (var process in SanitizeProcesses(report))
        {
            // The same identifier with a different start time is another process that reused the number.
            // When the start time is not known, the identifier alone has to do.
            var existing = beforeByPid[process.Pid].FirstOrDefault(run =>
                !stillRunning.Contains(run) && (process.StartedAt is null || SameMoment(run.StartedAt, process.StartedAt.Value)));

            if (existing is null)
            {
                existing = new ProcessRun(device.Id, process.Pid, process.Name, process.StartedAt ?? reportedAt);
                _activity.AddProcessRun(existing);
            }

            stillRunning.Add(existing);
            runsByPid[process.Pid] = existing;
        }

        foreach (var run in runningBefore.Where(run => !stillRunning.Contains(run)))
        {
            run.End(reportedAt);
        }

        return runsByPid;
    }

    private void ReconcilePorts(
        Device device,
        SystemActivityReport report,
        IReadOnlyList<ListeningPort> openBefore,
        Dictionary<int, ProcessRun> runsByPid,
        DateTimeOffset reportedAt,
        bool writeEvents)
    {
        var stillOpen = new HashSet<ListeningPort>();

        foreach (var reported in SanitizePorts(report))
        {
            runsByPid.TryGetValue(reported.Pid, out var owner);

            // A port taken over by another process is a new period: the old one closes, a new one opens.
            var existing = openBefore.FirstOrDefault(port =>
                !stillOpen.Contains(port)
                && port.Protocol == reported.Protocol
                && port.Port == reported.Port
                && port.LocalAddress == reported.LocalAddress
                && ReferenceEquals(port.ProcessRun, owner));

            if (existing is not null)
            {
                stillOpen.Add(existing);

                continue;
            }

            var opened = new ListeningPort(device.Id, reported.Protocol, reported.LocalAddress, reported.Port, owner, reportedAt);
            _activity.AddListeningPort(opened);

            if (writeEvents)
            {
                _history.AddEvent(new MonitoringEvent(
                    reportedAt,
                    MonitoringEventType.PortOpened,
                    EventSeverity.Info,
                    $"Device '{device.Name}' opened {reported.Protocol.ToString().ToUpperInvariant()} port {reported.Port} "
                    + $"on {reported.LocalAddress} ({owner?.Name ?? "unknown process"}).",
                    device.Id));
            }
        }

        foreach (var port in openBefore.Where(port => !stillOpen.Contains(port)))
        {
            port.Close(reportedAt);
        }
    }

    /// <summary>
    /// Stores usage only for the most demanding processes: the top ones by processor load and the top ones
    /// by memory, so that neither kind of load is missed.
    /// </summary>
    private static void AttachProcessUsage(
        SystemActivityReport report,
        DeviceSnapshot snapshot,
        Dictionary<int, ProcessRun> runsByPid,
        int topProcessCount)
    {
        var processes = SanitizeProcesses(report).ToList();

        var mostDemanding = processes
            .Where(process => process.CpuUsagePercent > 0)
            .OrderByDescending(process => process.CpuUsagePercent)
            .Take(topProcessCount)
            .Concat(processes.OrderByDescending(process => process.MemoryBytes).Take(topProcessCount))
            .DistinctBy(process => process.Pid);

        foreach (var process in mostDemanding)
        {
            snapshot.AddProcessUsage(new ProcessUsage(runsByPid[process.Pid], process.CpuUsagePercent, process.MemoryBytes));
        }
    }

    private static List<ActiveConnection> CreateConnections(
        Device device,
        SystemActivityReport report,
        Dictionary<int, ProcessRun> runsByPid)
    {
        return report.Connections
            .Where(connection => !string.IsNullOrWhiteSpace(connection.LocalAddress) && !string.IsNullOrWhiteSpace(connection.RemoteAddress))
            .Take(SystemActivityReport.MaxConnections)
            .Select(connection => new ActiveConnection(
                device.Id,
                connection.Protocol,
                Shorten(connection.LocalAddress, ListeningPort.MaxAddressLength),
                connection.LocalPort,
                Shorten(connection.RemoteAddress, ListeningPort.MaxAddressLength),
                connection.RemotePort,
                Shorten(connection.State ?? string.Empty, ActiveConnection.MaxStateLength),
                runsByPid.GetValueOrDefault(connection.Pid)))
            .ToList();
    }

    /// <summary>Returns the reported processes with implausible values corrected and duplicates removed.</summary>
    private static IEnumerable<ProcessReport> SanitizeProcesses(SystemActivityReport report)
    {
        return report.Processes
            .Where(process => process.Pid >= 0 && !string.IsNullOrWhiteSpace(process.Name))
            .DistinctBy(process => process.Pid)
            .Take(SystemActivityReport.MaxProcesses)
            .Select(process => process with
            {
                Name = Shorten(process.Name, ProcessRun.MaxNameLength),
                CpuUsagePercent = process.CpuUsagePercent is { } cpu && double.IsFinite(cpu) ? Math.Clamp(cpu, 0, 100) : null,
                MemoryBytes = Math.Max(0, process.MemoryBytes),
            });
    }

    /// <summary>
    /// Returns the reported ports that are worth recording. UDP ports in the dynamic range are left out:
    /// programs open and close them all the time for their own outgoing communication, so they are not
    /// services of the device and would only flood the history.
    /// </summary>
    private static IEnumerable<ListeningPortReport> SanitizePorts(SystemActivityReport report)
    {
        return report.ListeningPorts
            .Where(port => port.Port is >= 0 and <= 65535 && !string.IsNullOrWhiteSpace(port.LocalAddress))
            .Where(port => port.Protocol != TransportProtocol.Udp || port.Port < FirstDynamicPort)
            .Select(port => port with { LocalAddress = Shorten(port.LocalAddress, ListeningPort.MaxAddressLength) })
            .DistinctBy(port => (port.Protocol, port.LocalAddress, port.Port))
            .Take(SystemActivityReport.MaxListeningPorts);
    }

    private static string Shorten(string value, int maxLength)
    {
        var trimmed = value.Trim();

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    /// <summary>Start times pass through the database, which keeps them less precisely than the agent reports them.</summary>
    private static bool SameMoment(DateTimeOffset left, DateTimeOffset right)
    {
        return (left - right).Duration() < TimeSpan.FromMilliseconds(5);
    }
}
