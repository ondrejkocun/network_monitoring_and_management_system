using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Metrics;

/// <summary>
/// Reads the TCP and UDP tables of Windows together with the process that owns each entry
/// (the IP Helper functions GetExtendedTcpTable and GetExtendedUdpTable).
/// </summary>
public static class IpHelperTables
{
    private const int AddressFamilyIPv4 = 2;
    private const int AddressFamilyIPv6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private const uint ErrorInsufficientBuffer = 122;

    private const int TcpStateClosed = 1;
    private const int TcpStateListen = 2;
    private const int TcpStateTimeWait = 11;
    private const int TcpStateDeleteTcb = 12;

    private static readonly string[] TcpStateNames =
    [
        "UNKNOWN", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RECEIVED", "ESTABLISHED", "FIN_WAIT_1", "FIN_WAIT_2",
        "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB",
    ];

    private delegate uint TableFunction(IntPtr table, ref int size, bool sort, int addressFamily, int tableClass, uint reserved);

    /// <summary>Adds all listening ports and connections of this computer to the given activity.</summary>
    public static void ReadInto(SystemActivity activity)
    {
        ParseTcpTable(ReadTable(GetExtendedTcpTable, AddressFamilyIPv4, TcpTableOwnerPidAll), isIPv6: false, activity);
        ParseTcpTable(ReadTable(GetExtendedTcpTable, AddressFamilyIPv6, TcpTableOwnerPidAll), isIPv6: true, activity);
        ParseUdpTable(ReadTable(GetExtendedUdpTable, AddressFamilyIPv4, UdpTableOwnerPid), isIPv6: false, activity);
        ParseUdpTable(ReadTable(GetExtendedUdpTable, AddressFamilyIPv6, UdpTableOwnerPid), isIPv6: true, activity);
    }

    /// <summary>
    /// Parses a TCP table with owning process identifiers (MIB_TCPTABLE_OWNER_PID or MIB_TCP6TABLE_OWNER_PID).
    /// Entries in the listening state become listening ports, the other live ones connections.
    /// </summary>
    public static void ParseTcpTable(ReadOnlySpan<byte> table, bool isIPv6, SystemActivity activity)
    {
        // IPv4 row: state, local address, local port, remote address, remote port, process (6 x 4 bytes).
        // IPv6 row: local address (16), scope, local port, remote address (16), scope, remote port, state, process.
        var addressSize = isIPv6 ? 16 : 4;
        var rowSize = isIPv6 ? 56 : 24;

        foreach (var row in Rows(table, rowSize))
        {
            int state;
            ReadOnlySpan<byte> localAddress, localPort, remoteAddress, remotePort;
            uint pid;

            if (isIPv6)
            {
                localAddress = row[..16];
                localPort = row.Slice(20, 4);
                remoteAddress = row.Slice(24, 16);
                remotePort = row.Slice(44, 4);
                state = BinaryPrimitives.ReadInt32LittleEndian(row.Slice(48, 4));
                pid = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(52, 4));
            }
            else
            {
                state = BinaryPrimitives.ReadInt32LittleEndian(row[..4]);
                localAddress = row.Slice(4, addressSize);
                localPort = row.Slice(8, 4);
                remoteAddress = row.Slice(12, addressSize);
                remotePort = row.Slice(16, 4);
                pid = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(20, 4));
            }

            if (state == TcpStateListen)
            {
                activity.ListeningPorts.Add(new ListeningPortEntry
                {
                    Protocol = TransportProtocol.Tcp,
                    LocalAddress = new IPAddress(localAddress).ToString(),
                    Port = ReadPort(localPort),
                    Pid = pid,
                });
            }
            else if (state is not (TcpStateClosed or TcpStateTimeWait or TcpStateDeleteTcb))
            {
                // Entries that are closed or only waiting to be discarded are no longer connections
                // and no longer belong to any process.
                activity.Connections.Add(new ConnectionEntry
                {
                    Protocol = TransportProtocol.Tcp,
                    LocalAddress = new IPAddress(localAddress).ToString(),
                    LocalPort = ReadPort(localPort),
                    RemoteAddress = new IPAddress(remoteAddress).ToString(),
                    RemotePort = ReadPort(remotePort),
                    State = state > 0 && state < TcpStateNames.Length ? TcpStateNames[state] : TcpStateNames[0],
                    Pid = pid,
                });
            }
        }
    }

    /// <summary>
    /// Parses a UDP table with owning process identifiers (MIB_UDPTABLE_OWNER_PID or MIB_UDP6TABLE_OWNER_PID).
    /// UDP has no connections; every entry is a port a process receives on.
    /// </summary>
    public static void ParseUdpTable(ReadOnlySpan<byte> table, bool isIPv6, SystemActivity activity)
    {
        // IPv4 row: local address, local port, process. IPv6 row: local address (16), scope, local port, process.
        var rowSize = isIPv6 ? 28 : 12;

        foreach (var row in Rows(table, rowSize))
        {
            activity.ListeningPorts.Add(new ListeningPortEntry
            {
                Protocol = TransportProtocol.Udp,
                LocalAddress = new IPAddress(isIPv6 ? row[..16] : row[..4]).ToString(),
                Port = ReadPort(row.Slice(isIPv6 ? 20 : 4, 4)),
                Pid = BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(isIPv6 ? 24 : 8, 4)),
            });
        }
    }

    /// <summary>A port is stored in network byte order in the first two bytes of a four-byte field.</summary>
    private static uint ReadPort(ReadOnlySpan<byte> field) => BinaryPrimitives.ReadUInt16BigEndian(field);

    private static RowEnumerable Rows(ReadOnlySpan<byte> table, int rowSize) => new(table, rowSize);

    private static byte[] ReadTable(TableFunction function, int addressFamily, int tableClass)
    {
        var size = 0;
        var result = function(IntPtr.Zero, ref size, false, addressFamily, tableClass, 0);

        // The table can grow between the two calls, so the size is asked for again until it fits.
        for (var attempt = 0; attempt < 5 && result == ErrorInsufficientBuffer; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                result = function(buffer, ref size, false, addressFamily, tableClass, 0);

                if (result == 0)
                {
                    var table = new byte[size];
                    Marshal.Copy(buffer, table, 0, size);

                    return table;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        if (result == 0)
        {
            return [];
        }

        throw new Win32Exception((int)result);
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int addressFamily, int tableClass, uint reserved);

    /// <summary>Walks the rows of a table: a four-byte row count followed by rows of a fixed size.</summary>
    private readonly ref struct RowEnumerable(ReadOnlySpan<byte> table, int rowSize)
    {
        private readonly ReadOnlySpan<byte> _table = table;

        public Enumerator GetEnumerator() => new(_table, rowSize);

        public ref struct Enumerator
        {
            private readonly ReadOnlySpan<byte> _table;
            private readonly int _rowSize;
            private readonly int _count;
            private int _index;

            public Enumerator(ReadOnlySpan<byte> table, int rowSize)
            {
                _table = table;
                _rowSize = rowSize;
                _index = -1;

                // A truncated table yields only the rows that are completely present.
                var declared = table.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(table) : 0;
                _count = Math.Clamp(declared, 0, Math.Max(0, (table.Length - 4) / rowSize));
            }

            public ReadOnlySpan<byte> Current => _table.Slice(4 + (_index * _rowSize), _rowSize);

            public bool MoveNext() => ++_index < _count;
        }
    }
}
