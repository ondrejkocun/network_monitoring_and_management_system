using Google.Protobuf.WellKnownTypes;
using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>What runs on one device according to its agent's last report, formatted for display.</summary>
public sealed class DeviceActivityViewModel
{
    public DeviceActivityViewModel(DeviceActivityReply activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        Processes = activity.Processes.Select(process => new ProcessRowViewModel(process)).ToList();
        ListeningPorts = activity.ListeningPorts.Select(port => new ListeningPortRowViewModel(port)).ToList();
        Connections = activity.Connections.Select(connection => new ConnectionRowViewModel(connection)).ToList();
    }

    public IReadOnlyList<ProcessRowViewModel> Processes { get; }

    public IReadOnlyList<ListeningPortRowViewModel> ListeningPorts { get; }

    public IReadOnlyList<ConnectionRowViewModel> Connections { get; }

    public string ProcessesHeader => $"Procesy ({Processes.Count})";

    public string ListeningPortsHeader => $"Otvorené porty ({ListeningPorts.Count})";

    public string ConnectionsHeader => $"Spojenia ({Connections.Count})";

    public bool IsEmpty => Processes.Count == 0 && ListeningPorts.Count == 0 && Connections.Count == 0;

    internal static string FormatTime(Timestamp? timestamp)
    {
        return timestamp?.ToDateTimeOffset().ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "–";
    }

    /// <summary>Formats the owning process as "name (identifier)", or a dash when it is not known.</summary>
    internal static string FormatProcess(string name, uint pid)
    {
        return string.IsNullOrEmpty(name) ? "–" : $"{name} ({pid})";
    }
}

public sealed class ProcessRowViewModel
{
    public ProcessRowViewModel(ProcessDetail process)
    {
        Name = process.Name;
        Pid = process.Pid;
        StartedText = DeviceActivityViewModel.FormatTime(process.StartedAt);

        // Usage is recorded only for the most demanding processes; the others show none.
        CpuText = process.HasUsage && process.HasCpuUsagePercent ? ByteSize.FormatPercent(process.CpuUsagePercent) : string.Empty;
        MemoryText = process.HasUsage ? ByteSize.Format(process.MemoryBytes) : string.Empty;
    }

    public string Name { get; }

    public uint Pid { get; }

    public string StartedText { get; }

    public string CpuText { get; }

    public string MemoryText { get; }
}

public sealed class ListeningPortRowViewModel
{
    public ListeningPortRowViewModel(ListeningPortDetail port)
    {
        Port = port.Port;
        Protocol = port.Protocol;
        LocalAddress = port.LocalAddress;
        ProcessText = DeviceActivityViewModel.FormatProcess(port.ProcessName, port.Pid);
        OpenedText = DeviceActivityViewModel.FormatTime(port.OpenedAt);
    }

    public uint Port { get; }

    public string Protocol { get; }

    public string LocalAddress { get; }

    public string ProcessText { get; }

    public string OpenedText { get; }
}

public sealed class ConnectionRowViewModel
{
    public ConnectionRowViewModel(ConnectionDetail connection)
    {
        ProcessText = DeviceActivityViewModel.FormatProcess(connection.ProcessName, connection.Pid);
        Protocol = connection.Protocol;
        LocalEndpoint = FormatEndpoint(connection.LocalAddress, connection.LocalPort);
        RemoteEndpoint = FormatEndpoint(connection.RemoteAddress, connection.RemotePort);
        State = connection.State;
    }

    public string ProcessText { get; }

    public string Protocol { get; }

    public string LocalEndpoint { get; }

    public string RemoteEndpoint { get; }

    public string State { get; }

    /// <summary>An IPv6 address contains colons itself, so it is put in brackets before the port.</summary>
    private static string FormatEndpoint(string address, uint port)
    {
        return address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
    }
}
