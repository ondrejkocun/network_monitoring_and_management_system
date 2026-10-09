namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class DeviceActivityRecorderTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BootTime = Start.AddHours(-5);

    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly InMemoryDeviceActivityRepository _activity;
    private readonly DeviceActivityRecorder _recorder;
    private readonly Device _device = new(Guid.NewGuid(), "PC-01", MonitoringMode.Agent, "pc-01", null, Start);

    public DeviceActivityRecorderTests()
    {
        _activity = new InMemoryDeviceActivityRepository(_history);
        _recorder = new DeviceActivityRecorder(_activity, _history);
    }

    [Fact]
    public async Task FirstReport_RecordsEverythingAsBaseline_WithoutEvents()
    {
        await ReportAsync(
            Start,
            processes: [Process(100, "nginx"), Process(200, "sqlservr")],
            ports: [Tcp(80, pid: 100), Tcp(1433, pid: 200)]);

        Assert.Equal(2, _activity.ProcessRuns.Count);
        Assert.All(_activity.ProcessRuns, run => Assert.True(run.IsRunning));
        Assert.Equal(BootTime, _activity.ProcessRuns[0].StartedAt);

        Assert.Equal(2, _activity.ListeningPorts.Count);
        Assert.Equal("nginx", _activity.ListeningPorts.Single(port => port.Port == 80).ProcessRun!.Name);
        Assert.Equal(Start, _activity.ListeningPorts[0].OpenedAt);

        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task UnchangedReport_AddsNothing()
    {
        ProcessReport[] processes = [Process(100, "nginx")];
        ListeningPortReport[] ports = [Tcp(80, pid: 100)];
        await ReportAsync(Start, processes, ports);

        await ReportAsync(Start.AddMinutes(1), processes, ports);

        Assert.Single(_activity.ProcessRuns);
        Assert.Single(_activity.ListeningPorts);
        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task NewProcess_StartsRun_AndMissingProcessEndsItsRun()
    {
        await ReportAsync(Start, processes: [Process(100, "nginx"), Process(200, "notepad")]);

        await ReportAsync(Start.AddMinutes(1), processes: [Process(100, "nginx"), Process(300, "calc", Start.AddSeconds(30))]);

        var notepad = _activity.ProcessRuns.Single(run => run.Name == "notepad");
        Assert.Equal(Start.AddMinutes(1), notepad.EndedAt);

        var calc = _activity.ProcessRuns.Single(run => run.Name == "calc");
        Assert.True(calc.IsRunning);
        Assert.Equal(Start.AddSeconds(30), calc.StartedAt);

        Assert.True(_activity.ProcessRuns.Single(run => run.Name == "nginx").IsRunning);
    }

    [Fact]
    public async Task ReusedProcessIdentifier_IsADifferentRun()
    {
        await ReportAsync(Start, processes: [Process(100, "notepad")]);

        await ReportAsync(Start.AddMinutes(1), processes: [Process(100, "calc", Start.AddSeconds(40))]);

        Assert.Equal(2, _activity.ProcessRuns.Count);
        Assert.False(_activity.ProcessRuns.Single(run => run.Name == "notepad").IsRunning);
        Assert.True(_activity.ProcessRuns.Single(run => run.Name == "calc").IsRunning);
    }

    [Fact]
    public async Task ProcessWithoutKnownStartTime_IsMatchedByIdentifierAlone()
    {
        await ReportAsync(Start, processes: [new ProcessReport(4, "System", null, null, 1000)]);

        await ReportAsync(Start.AddMinutes(1), processes: [new ProcessReport(4, "System", null, null, 1000)]);

        var run = Assert.Single(_activity.ProcessRuns);
        Assert.Equal(Start, run.StartedAt);
        Assert.True(run.IsRunning);
    }

    [Fact]
    public async Task StartTimeThatLostPrecisionInStorage_StillMatches()
    {
        var precise = BootTime.AddTicks(1234567);
        await ReportAsync(Start, processes: [Process(100, "nginx", precise)]);

        // PostgreSQL keeps microseconds, the agent reports 100 ns ticks.
        await ReportAsync(Start.AddMinutes(1), processes: [Process(100, "nginx", precise.AddTicks(-7))]);

        Assert.Single(_activity.ProcessRuns);
    }

    [Fact]
    public async Task NewPort_IsRecorded_WithEventNamingPortAndProcess()
    {
        await ReportAsync(Start, processes: [Process(100, "nginx")], ports: [Tcp(80, pid: 100)]);

        await ReportAsync(
            Start.AddMinutes(1),
            processes: [Process(100, "nginx"), Process(300, "backdoor", Start.AddSeconds(30))],
            ports: [Tcp(80, pid: 100), Tcp(4444, pid: 300)]);

        var opened = _activity.ListeningPorts.Single(port => port.Port == 4444);
        Assert.Equal(Start.AddMinutes(1), opened.OpenedAt);
        Assert.Equal("backdoor", opened.ProcessRun!.Name);

        var portEvent = Assert.Single(_history.Events);
        Assert.Equal(MonitoringEventType.PortOpened, portEvent.Type);
        Assert.Equal(_device.Id, portEvent.DeviceId);
        Assert.Contains("TCP port 4444", portEvent.Message);
        Assert.Contains("backdoor", portEvent.Message);
    }

    [Fact]
    public async Task MissingPort_IsClosed()
    {
        await ReportAsync(Start, processes: [Process(100, "nginx")], ports: [Tcp(80, pid: 100), Tcp(443, pid: 100)]);

        await ReportAsync(Start.AddMinutes(1), processes: [Process(100, "nginx")], ports: [Tcp(80, pid: 100)]);

        Assert.Equal(Start.AddMinutes(1), _activity.ListeningPorts.Single(port => port.Port == 443).ClosedAt);
        Assert.True(_activity.ListeningPorts.Single(port => port.Port == 80).IsOpen);
    }

    [Fact]
    public async Task PortTakenOverByAnotherProcess_ClosesOnePeriodAndOpensAnother()
    {
        await ReportAsync(Start, processes: [Process(100, "nginx")], ports: [Tcp(80, pid: 100)]);

        await ReportAsync(
            Start.AddMinutes(1),
            processes: [Process(500, "apache", Start.AddSeconds(30))],
            ports: [Tcp(80, pid: 500)]);

        Assert.Equal(2, _activity.ListeningPorts.Count);
        Assert.Equal("nginx", _activity.ListeningPorts.Single(port => !port.IsOpen).ProcessRun!.Name);
        Assert.Equal("apache", _activity.ListeningPorts.Single(port => port.IsOpen).ProcessRun!.Name);
        Assert.Single(_history.Events);
    }

    [Fact]
    public async Task UdpPortsInDynamicRange_AreIgnored_ButServicePortsAreKept()
    {
        await ReportAsync(
            Start,
            processes: [Process(100, "dns")],
            ports:
            [
                new ListeningPortReport(TransportProtocol.Udp, "0.0.0.0", 53, 100),
                new ListeningPortReport(TransportProtocol.Udp, "0.0.0.0", DeviceActivityRecorder.FirstDynamicPort, 100),
                new ListeningPortReport(TransportProtocol.Udp, "0.0.0.0", 60000, 100),
                new ListeningPortReport(TransportProtocol.Tcp, "0.0.0.0", 60000, 100),
            ]);

        Assert.Equal(
            [(TransportProtocol.Udp, 53), (TransportProtocol.Tcp, 60000)],
            _activity.ListeningPorts.Select(port => (port.Protocol, port.Port)));
    }

    [Fact]
    public async Task Usage_IsStoredOnlyForMostDemandingProcesses()
    {
        var snapshot = await ReportAsync(
            Start,
            processes:
            [
                Process(1, "idle-small", cpu: 0, memory: 10),
                Process(2, "busy", cpu: 80, memory: 20),
                Process(3, "big", cpu: 0, memory: 9000),
                Process(4, "medium", cpu: 5, memory: 500),
                Process(5, "unknown-load", cpu: null, memory: 30),
            ],
            topProcessCount: 1);

        // The single top one by processor and the single top one by memory.
        Assert.Equal(["busy", "big"], snapshot.ProcessUsages.Select(usage => usage.ProcessRun.Name));
        Assert.Equal(80.0, snapshot.ProcessUsages[0].CpuUsagePercent);
        Assert.Equal(9000L, snapshot.ProcessUsages[1].MemoryBytes);
        Assert.Equal(5, _activity.ProcessRuns.Count);
    }

    [Fact]
    public async Task Connections_AreReplacedByTheLatestReport()
    {
        await ReportAsync(
            Start,
            processes: [Process(100, "chrome")],
            connections: [Connection("93.184.216.34", 443, pid: 100), Connection("1.1.1.1", 443, pid: 100)]);

        await ReportAsync(
            Start.AddMinutes(1),
            processes: [Process(100, "chrome")],
            connections: [Connection("8.8.8.8", 53, pid: 100)]);

        var connection = Assert.Single(_activity.Connections);
        Assert.Equal("8.8.8.8", connection.RemoteAddress);
        Assert.Equal("chrome", connection.ProcessRun!.Name);
        Assert.Equal("ESTABLISHED", connection.State);
    }

    [Fact]
    public async Task ImplausibleValues_AreCorrected_InsteadOfRejectingTheReport()
    {
        var longName = new string('x', 5000);

        var snapshot = await ReportAsync(
            Start,
            processes:
            [
                Process(100, longName, cpu: 500, memory: -5),
                Process(100, "duplicate-pid"),
                Process(-1, "negative-pid"),
                Process(200, "  "),
            ],
            ports: [Tcp(70000, pid: 100), Tcp(80, pid: 999), new ListeningPortReport(TransportProtocol.Tcp, " ", 81, 100)],
            connections: [new ConnectionReport(TransportProtocol.Tcp, "10.0.0.1", 99999, longName, -1, longName, 100)]);

        var run = Assert.Single(_activity.ProcessRuns);
        Assert.Equal(ProcessRun.MaxNameLength, run.Name.Length);

        var usage = Assert.Single(snapshot.ProcessUsages);
        Assert.Equal(100.0, usage.CpuUsagePercent);
        Assert.Equal(0L, usage.MemoryBytes);

        // The port of an unknown process is kept, just without an owner.
        var port = Assert.Single(_activity.ListeningPorts);
        Assert.Equal(80, port.Port);
        Assert.Null(port.ProcessRun);

        var connection = Assert.Single(_activity.Connections);
        Assert.Equal(65535, connection.LocalPort);
        Assert.Equal(0, connection.RemotePort);
        Assert.Equal(ListeningPort.MaxAddressLength, connection.RemoteAddress.Length);
        Assert.Equal(ActiveConnection.MaxStateLength, connection.State.Length);
    }

    [Fact]
    public async Task Activity_IsReturnedForDisplay_WithMostDemandingProcessesFirst()
    {
        await ReportAsync(
            Start,
            processes: [Process(1, "zeta", cpu: 0, memory: 10), Process(2, "busy", cpu: 80, memory: 20), Process(3, "alpha", cpu: 0, memory: 5)],
            ports: [Tcp(443, pid: 2), Tcp(80, pid: 2)],
            connections: [Connection("8.8.8.8", 53, pid: 2)],
            topProcessCount: 1);

        var activity = DeviceActivityDto.Create(
            await _activity.GetRunningProcessesAsync(_device.Id),
            await _activity.GetLatestProcessUsagesAsync(_device.Id),
            await _activity.GetOpenPortsAsync(_device.Id),
            await _activity.GetConnectionsAsync(_device.Id));

        Assert.Equal(["busy", "alpha", "zeta"], activity.Processes.Select(process => process.Name));
        Assert.True(activity.Processes[0].HasUsage);
        Assert.Equal(80.0, activity.Processes[0].CpuUsagePercent);
        Assert.False(activity.Processes[1].HasUsage);

        Assert.Equal([80, 443], activity.ListeningPorts.Select(port => port.Port));
        Assert.Equal("busy", activity.ListeningPorts[0].ProcessName);
        Assert.Equal(2, activity.ListeningPorts[0].Pid);

        Assert.Equal("busy", Assert.Single(activity.Connections).ProcessName);
    }

    private async Task<DeviceSnapshot> ReportAsync(
        DateTimeOffset reportedAt,
        ProcessReport[]? processes = null,
        ListeningPortReport[]? ports = null,
        ConnectionReport[]? connections = null,
        int topProcessCount = 10)
    {
        var snapshot = new DeviceSnapshot(_device.Id, reportedAt, DeviceStatus.Online);
        _history.AddSnapshot(snapshot);

        await _recorder.RecordAsync(
            _device,
            new SystemActivityReport(processes ?? [], ports ?? [], connections ?? []),
            snapshot,
            topProcessCount,
            reportedAt);

        return snapshot;
    }

    private static ProcessReport Process(int pid, string name, DateTimeOffset? startedAt = null, double? cpu = 0, long memory = 1000)
    {
        return new ProcessReport(pid, name, startedAt ?? BootTime, cpu, memory);
    }

    private static ListeningPortReport Tcp(int port, int pid) => new(TransportProtocol.Tcp, "0.0.0.0", port, pid);

    private static ConnectionReport Connection(string remoteAddress, int remotePort, int pid)
    {
        return new ConnectionReport(TransportProtocol.Tcp, "192.168.50.20", 50000, remoteAddress, remotePort, "ESTABLISHED", pid);
    }
}
