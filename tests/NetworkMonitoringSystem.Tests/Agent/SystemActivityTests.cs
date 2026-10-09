namespace NetworkMonitoringSystem.Tests.Agent;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetworkMonitoringSystem.Agent.Metrics;
using NetworkMonitoringSystem.Contracts.Agents;

public class SystemActivityTests
{
    [Fact]
    public void ParseTcpTable_IPv4_SeparatesListeningPortsFromConnections()
    {
        var table = Table(
            TcpRowV4(state: 2, "0.0.0.0", 80, "0.0.0.0", 0, pid: 1234),
            TcpRowV4(state: 5, "192.168.50.20", 50123, "93.184.216.34", 443, pid: 4321));
        var activity = new SystemActivity();

        IpHelperTables.ParseTcpTable(table, isIPv6: false, activity);

        var port = Assert.Single(activity.ListeningPorts);
        Assert.Equal(TransportProtocol.Tcp, port.Protocol);
        Assert.Equal("0.0.0.0", port.LocalAddress);
        Assert.Equal(80U, port.Port);
        Assert.Equal(1234U, port.Pid);

        var connection = Assert.Single(activity.Connections);
        Assert.Equal("192.168.50.20", connection.LocalAddress);
        Assert.Equal(50123U, connection.LocalPort);
        Assert.Equal("93.184.216.34", connection.RemoteAddress);
        Assert.Equal(443U, connection.RemotePort);
        Assert.Equal("ESTABLISHED", connection.State);
        Assert.Equal(4321U, connection.Pid);
    }

    [Theory]
    [InlineData(1)]   // CLOSED
    [InlineData(11)]  // TIME_WAIT
    [InlineData(12)]  // DELETE_TCB
    public void ParseTcpTable_LeavesOutEntriesThatAreNoLongerConnections(int state)
    {
        var table = Table(TcpRowV4(state, "127.0.0.1", 49748, "127.0.0.1", 5432, pid: 0));
        var activity = new SystemActivity();

        IpHelperTables.ParseTcpTable(table, isIPv6: false, activity);

        Assert.Empty(activity.Connections);
        Assert.Empty(activity.ListeningPorts);
    }

    [Fact]
    public void ParseTcpTable_IPv6_ReadsAddressesPortsStateAndProcess()
    {
        var row = new byte[56];
        IPAddress.IPv6Any.GetAddressBytes().CopyTo(row, 0);
        WritePort(row.AsSpan(20), 8080);
        IPAddress.IPv6Any.GetAddressBytes().CopyTo(row, 24);
        BinaryPrimitives.WriteInt32LittleEndian(row.AsSpan(48), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(52), 777);
        var activity = new SystemActivity();

        IpHelperTables.ParseTcpTable(Table(row), isIPv6: true, activity);

        var port = Assert.Single(activity.ListeningPorts);
        Assert.Equal("::", port.LocalAddress);
        Assert.Equal(8080U, port.Port);
        Assert.Equal(777U, port.Pid);
        Assert.Empty(activity.Connections);
    }

    [Fact]
    public void ParseUdpTable_ReadsEveryEntryAsPort()
    {
        var v4 = new byte[12];
        IPAddress.Parse("127.0.0.1").GetAddressBytes().CopyTo(v4, 0);
        WritePort(v4.AsSpan(4), 53);
        BinaryPrimitives.WriteUInt32LittleEndian(v4.AsSpan(8), 55);

        var v6 = new byte[28];
        IPAddress.IPv6Loopback.GetAddressBytes().CopyTo(v6, 0);
        WritePort(v6.AsSpan(20), 5353);
        BinaryPrimitives.WriteUInt32LittleEndian(v6.AsSpan(24), 66);

        var activity = new SystemActivity();
        IpHelperTables.ParseUdpTable(Table(v4), isIPv6: false, activity);
        IpHelperTables.ParseUdpTable(Table(v6), isIPv6: true, activity);

        Assert.Equal(2, activity.ListeningPorts.Count);
        Assert.All(activity.ListeningPorts, port => Assert.Equal(TransportProtocol.Udp, port.Protocol));
        Assert.Equal(("127.0.0.1", 53U, 55U), (activity.ListeningPorts[0].LocalAddress, activity.ListeningPorts[0].Port, activity.ListeningPorts[0].Pid));
        Assert.Equal(("::1", 5353U, 66U), (activity.ListeningPorts[1].LocalAddress, activity.ListeningPorts[1].Port, activity.ListeningPorts[1].Pid));
    }

    [Fact]
    public void ParseTcpTable_IgnoresRowsThatAreNotCompletelyPresent()
    {
        // The header announces three rows, but only one is there.
        var table = new byte[4 + 24 + 10];
        BinaryPrimitives.WriteInt32LittleEndian(table, 3);
        TcpRowV4(state: 2, "0.0.0.0", 22, "0.0.0.0", 0, pid: 1).CopyTo(table, 4);
        var activity = new SystemActivity();

        IpHelperTables.ParseTcpTable(table, isIPv6: false, activity);
        IpHelperTables.ParseTcpTable([], isIPv6: false, activity);

        Assert.Equal(22U, Assert.Single(activity.ListeningPorts).Port);
    }

    [Theory]
    [InlineData(500, 1000, 1, 50.0)]
    [InlineData(500, 1000, 4, 12.5)]
    [InlineData(4000, 1000, 4, 100.0)]
    [InlineData(9000, 1000, 4, 100.0)]
    public void ComputeUsagePercent_IsShareOfAllProcessors(int usedMs, int elapsedMs, int processors, double expected)
    {
        var earlier = new ProcessorSample(TimeSpan.FromSeconds(10), 0);
        var later = new ProcessorSample(
            TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(usedMs),
            Stopwatch.Frequency * elapsedMs / 1000);

        var usage = WindowsSystemActivityCollector.ComputeUsagePercent(earlier, later, processors);

        Assert.NotNull(usage);
        Assert.Equal(expected, usage.Value, precision: 6);
    }

    [Fact]
    public void ComputeUsagePercent_ReturnsNull_WhenNoTimePassed()
    {
        var sample = new ProcessorSample(TimeSpan.FromSeconds(10), 1000);

        Assert.Null(WindowsSystemActivityCollector.ComputeUsagePercent(sample, sample, 4));
    }

    [Fact]
    public async Task WindowsCollector_FindsThisProcess_AndPortItListensOn()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var listeningPort = (uint)((IPEndPoint)listener.LocalEndpoint).Port;

        var collector = new WindowsSystemActivityCollector();
        await Task.Delay(300);

        var activity = collector.Collect();

        var thisProcess = Assert.Single(activity.Processes, process => process.Pid == (uint)Environment.ProcessId);
        Assert.Equal(Process.GetCurrentProcess().ProcessName, thisProcess.Name);
        Assert.NotNull(thisProcess.StartedAt);
        Assert.True(thisProcess.HasCpuUsagePercent);
        Assert.InRange(thisProcess.CpuUsagePercent, 0, 100);
        Assert.True(thisProcess.MemoryBytes > 0);

        Assert.DoesNotContain(activity.Processes, process => process.Pid == 0);
        Assert.True(activity.Processes.Count > 10);

        // The port opened above is reported as owned by this very process.
        var port = Assert.Single(activity.ListeningPorts, port => port.Protocol == TransportProtocol.Tcp && port.Port == listeningPort);
        Assert.Equal("127.0.0.1", port.LocalAddress);
        Assert.Equal((uint)Environment.ProcessId, port.Pid);
    }

    private static byte[] Table(params byte[][] rows)
    {
        var table = new byte[4 + rows.Sum(row => row.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(table, rows.Length);

        var offset = 4;

        foreach (var row in rows)
        {
            row.CopyTo(table, offset);
            offset += row.Length;
        }

        return table;
    }

    private static byte[] TcpRowV4(int state, string localAddress, ushort localPort, string remoteAddress, ushort remotePort, uint pid)
    {
        var row = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(row, state);
        IPAddress.Parse(localAddress).GetAddressBytes().CopyTo(row, 4);
        WritePort(row.AsSpan(8), localPort);
        IPAddress.Parse(remoteAddress).GetAddressBytes().CopyTo(row, 12);
        WritePort(row.AsSpan(16), remotePort);
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(20), pid);

        return row;
    }

    /// <summary>Windows stores a port in network byte order in the first two bytes of a four-byte field.</summary>
    private static void WritePort(Span<byte> field, ushort port) => BinaryPrimitives.WriteUInt16BigEndian(field, port);
}
