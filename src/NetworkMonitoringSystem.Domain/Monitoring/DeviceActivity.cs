namespace NetworkMonitoringSystem.Domain.Monitoring;

public enum TransportProtocol
{
    Tcp = 0,
    Udp = 1,
}

/// <summary>
/// One run of a process on a device, from its start to its end. A run without an end is still running.
/// </summary>
public sealed class ProcessRun
{
    public const int MaxNameLength = 260;

    public ProcessRun(Guid deviceId, int pid, string name, DateTimeOffset startedAt)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(pid);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, MaxNameLength, nameof(name));

        DeviceId = deviceId;
        Pid = pid;
        Name = name;
        StartedAt = startedAt;
    }

    public long Id { get; private set; }

    public Guid DeviceId { get; }

    /// <summary>Process identifier. Windows reuses identifiers, so only the pair with the start time is unique.</summary>
    public int Pid { get; }

    public string Name { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsRunning => EndedAt is null;

    public void End(DateTimeOffset endedAt)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The process run has already ended.");
        }

        // A process cannot end before it started, even when the two clocks disagree slightly.
        EndedAt = endedAt < StartedAt ? StartedAt : endedAt;
    }
}

/// <summary>How much a process used at the moment of one snapshot.</summary>
public sealed class ProcessUsage
{
    public ProcessUsage(ProcessRun processRun, double? cpuUsagePercent, long memoryBytes)
    {
        ArgumentNullException.ThrowIfNull(processRun);
        ArgumentOutOfRangeException.ThrowIfNegative(memoryBytes);

        if (cpuUsagePercent is < 0 or > 100 || (cpuUsagePercent is { } cpu && double.IsNaN(cpu)))
        {
            throw new ArgumentOutOfRangeException(nameof(cpuUsagePercent), cpuUsagePercent, "CPU usage must be between 0 and 100 percent.");
        }

        ProcessRun = processRun;
        CpuUsagePercent = cpuUsagePercent;
        MemoryBytes = memoryBytes;
    }

    private ProcessUsage()
    {
        ProcessRun = null!;
    }

    public long Id { get; private set; }

    public ProcessRun ProcessRun { get; private set; }

    public double? CpuUsagePercent { get; private set; }

    public long MemoryBytes { get; private set; }
}

/// <summary>
/// A period during which a port on a device was open for incoming communication.
/// A port without a closing time is still open.
/// </summary>
public sealed class ListeningPort
{
    public const int MaxAddressLength = 64;

    public ListeningPort(
        Guid deviceId,
        TransportProtocol protocol,
        string localAddress,
        int port,
        ProcessRun? processRun,
        DateTimeOffset openedAt)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(localAddress);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(localAddress.Length, MaxAddressLength, nameof(localAddress));
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        DeviceId = deviceId;
        Protocol = protocol;
        LocalAddress = localAddress;
        Port = port;
        ProcessRun = processRun;
        OpenedAt = openedAt;
    }

    private ListeningPort()
    {
        LocalAddress = null!;
    }

    public long Id { get; private set; }

    public Guid DeviceId { get; private set; }

    public TransportProtocol Protocol { get; private set; }

    public string LocalAddress { get; private set; }

    public int Port { get; private set; }

    /// <summary>The process that opened the port, when the agent could tell.</summary>
    public ProcessRun? ProcessRun { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public bool IsOpen => ClosedAt is null;

    public void Close(DateTimeOffset closedAt)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("The port has already been closed.");
        }

        ClosedAt = closedAt < OpenedAt ? OpenedAt : closedAt;
    }
}

/// <summary>A connection a device had open when its agent last reported. Only the current state is kept.</summary>
public sealed class ActiveConnection
{
    public const int MaxStateLength = 32;

    public ActiveConnection(
        Guid deviceId,
        TransportProtocol protocol,
        string localAddress,
        int localPort,
        string remoteAddress,
        int remotePort,
        string state,
        ProcessRun? processRun)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(localAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteAddress);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(localAddress.Length, ListeningPort.MaxAddressLength, nameof(localAddress));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(remoteAddress.Length, ListeningPort.MaxAddressLength, nameof(remoteAddress));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(state?.Length ?? 0, MaxStateLength, nameof(state));

        DeviceId = deviceId;
        Protocol = protocol;
        LocalAddress = localAddress;
        LocalPort = Math.Clamp(localPort, 0, 65535);
        RemoteAddress = remoteAddress;
        RemotePort = Math.Clamp(remotePort, 0, 65535);
        State = state ?? string.Empty;
        ProcessRun = processRun;
    }

    private ActiveConnection()
    {
        LocalAddress = null!;
        RemoteAddress = null!;
        State = null!;
    }

    public long Id { get; private set; }

    public Guid DeviceId { get; private set; }

    public TransportProtocol Protocol { get; private set; }

    public string LocalAddress { get; private set; }

    public int LocalPort { get; private set; }

    public string RemoteAddress { get; private set; }

    public int RemotePort { get; private set; }

    public string State { get; private set; }

    /// <summary>The process that owns the connection, when the agent could tell.</summary>
    public ProcessRun? ProcessRun { get; private set; }
}
