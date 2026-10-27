using System.Globalization;
using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>
/// Which ports were open and which processes ran on the selected device in the chosen period, with their start,
/// end and owning process. The filter stays when new data arrives.
/// </summary>
public sealed class DeviceActivityHistoryViewModel : ViewModelBase
{
    private IReadOnlyList<PortPeriodRowViewModel> _allPorts = [];
    private IReadOnlyList<ProcessRunRowViewModel> _allProcesses = [];
    private IReadOnlyList<PortPeriodRowViewModel> _ports = [];
    private IReadOnlyList<ProcessRunRowViewModel> _processes = [];
    private string _filterText = string.Empty;
    private string _summaryText = string.Empty;
    private bool _truncated;

    public DeviceActivityHistoryViewModel(DeviceActivityHistoryReply history, DateTimeOffset now)
    {
        Show(history, now);
    }

    /// <summary>Shows only ports and processes whose name, process identifier, port or address contains this text.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                Apply();
            }
        }
    }

    /// <summary>Periods in which a port was open, the most recently opened first.</summary>
    public IReadOnlyList<PortPeriodRowViewModel> Ports
    {
        get => _ports;
        private set => SetProperty(ref _ports, value);
    }

    /// <summary>Runs of processes, the most recently started first.</summary>
    public IReadOnlyList<ProcessRunRowViewModel> Processes
    {
        get => _processes;
        private set => SetProperty(ref _processes, value);
    }

    public string PortsHeader => $"Porty otvorené v období ({Ports.Count})";

    public string ProcessesHeader => $"Procesy bežiace v období ({Processes.Count})";

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>Replaces the data with newly loaded history; the filter stays as it is.</summary>
    /// <param name="now">Durations of what is still running or open are measured to this moment.</param>
    public void Show(DeviceActivityHistoryReply history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);

        _allPorts = history.Ports.Select(port => new PortPeriodRowViewModel(port, now)).ToList();
        _allProcesses = history.Processes.Select(run => new ProcessRunRowViewModel(run, now)).ToList();
        _truncated = history.Truncated;
        Apply();
    }

    private void Apply()
    {
        var text = FilterText.Trim();

        Ports = _allPorts.Where(row => text.Length == 0 || row.Matches(text)).ToList();
        Processes = _allProcesses.Where(row => text.Length == 0 || row.Matches(text)).ToList();

        var closed = _allPorts.Count(row => !row.IsOpen);
        var ended = _allProcesses.Count(row => !row.IsRunning);
        SummaryText = $"V období: {_allPorts.Count} období portov ({closed} už zatvorených), {_allProcesses.Count} behov procesov ({ended} už skončených)."
            + (_truncated ? " Obdobie má viac záznamov, zobrazujú sa najnovšie; pre staršie zvoľte kratšie obdobie." : string.Empty);

        OnPropertyChanged(nameof(PortsHeader));
        OnPropertyChanged(nameof(ProcessesHeader));
    }
}

public sealed class PortPeriodRowViewModel
{
    public PortPeriodRowViewModel(PortPeriodInfo port, DateTimeOffset now)
    {
        Port = port.Port;
        Protocol = port.Protocol;
        LocalAddress = port.LocalAddress;
        ProcessText = DeviceActivityViewModel.FormatProcess(port.ProcessName, port.Pid);
        IsOpen = port.ClosedAt is null;

        var opened = port.OpenedAt.ToDateTimeOffset();
        var closed = port.ClosedAt?.ToDateTimeOffset();
        OpenedText = DeviceHistoryViewModel.FormatTime(opened);
        ClosedText = closed is { } value ? DeviceHistoryViewModel.FormatTime(value) : "stále otvorený";
        DurationText = DeviceHistoryViewModel.FormatDuration((closed ?? now) - opened);
    }

    public uint Port { get; }

    public string Protocol { get; }

    public string LocalAddress { get; }

    public string ProcessText { get; }

    public bool IsOpen { get; }

    public string OpenedText { get; }

    public string ClosedText { get; }

    public string DurationText { get; }

    public bool Matches(string text)
    {
        return Port.ToString(CultureInfo.InvariantCulture) == text
            || Protocol.Contains(text, StringComparison.OrdinalIgnoreCase)
            || LocalAddress.Contains(text, StringComparison.OrdinalIgnoreCase)
            || ProcessText.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProcessRunRowViewModel
{
    public ProcessRunRowViewModel(ProcessRunInfo run, DateTimeOffset now)
    {
        Name = run.Name;
        Pid = run.Pid;
        IsRunning = run.EndedAt is null;

        var started = run.StartedAt.ToDateTimeOffset();
        var ended = run.EndedAt?.ToDateTimeOffset();
        StartedText = DeviceHistoryViewModel.FormatTime(started);
        EndedText = ended is { } value ? DeviceHistoryViewModel.FormatTime(value) : "stále beží";
        DurationText = DeviceHistoryViewModel.FormatDuration((ended ?? now) - started);
    }

    public string Name { get; }

    public uint Pid { get; }

    public bool IsRunning { get; }

    public string StartedText { get; }

    public string EndedText { get; }

    public string DurationText { get; }

    public bool Matches(string text)
    {
        return Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Pid.ToString(CultureInfo.InvariantCulture) == text;
    }
}
