using System.Globalization;
using System.Net;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Traffic;

/// <summary>One IP packet as TShark describes it. Its contents are never read.</summary>
public sealed record CapturedPacket(
    int Length,
    IPAddress Source,
    IPAddress Destination,
    TrafficTransport Transport,
    int? SourcePort,
    int? DestinationPort,
    string Protocol)
{
    /// <summary>
    /// The fields TShark prints for every packet, in this order. Only headers are asked for: sizes, addresses,
    /// ports and the name of the highest protocol TShark recognised.
    /// </summary>
    public static readonly IReadOnlyList<string> TsharkFields =
    [
        "frame.len",
        "ip.src",
        "ip.dst",
        "ipv6.src",
        "ipv6.dst",
        "ip.proto",
        "ipv6.nxt",
        "tcp.srcport",
        "tcp.dstport",
        "udp.srcport",
        "udp.dstport",
        "_ws.col.protocol",
    ];

    /// <summary>
    /// Reads one line of TShark's tab-separated output. Returns null for frames that are not IP traffic,
    /// such as ARP, and for lines that cannot be read.
    /// </summary>
    public static CapturedPacket? TryParse(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var fields = line.Split('\t');

        if (fields.Length < TsharkFields.Count || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            return null;
        }

        var isIPv4 = fields[1].Length > 0;
        var source = ParseAddress(isIPv4 ? fields[1] : fields[3]);
        var destination = ParseAddress(isIPv4 ? fields[2] : fields[4]);

        if (source is null || destination is null)
        {
            return null;
        }

        var transport = ParseNumber(isIPv4 ? fields[5] : fields[6]) switch
        {
            6 => TrafficTransport.Tcp,
            17 => TrafficTransport.Udp,
            1 or 58 => TrafficTransport.Icmp,
            _ => TrafficTransport.Other,
        };

        var (sourcePort, destinationPort) = transport switch
        {
            TrafficTransport.Tcp => (ParseNumber(fields[7]), ParseNumber(fields[8])),
            TrafficTransport.Udp => (ParseNumber(fields[9]), ParseNumber(fields[10])),
            _ => ((int?)null, (int?)null),
        };

        // TShark escapes some characters in column text; only a plain name is wanted.
        var protocol = fields[11].Replace("\\", string.Empty, StringComparison.Ordinal).Trim();

        return new CapturedPacket(
            length,
            source,
            destination,
            transport,
            sourcePort,
            destinationPort,
            protocol.Length > 0 ? protocol : transport.ToString().ToUpperInvariant());
    }

    /// <summary>The address without a scope, so that the same address always compares equal.</summary>
    public static IPAddress WithoutScope(IPAddress address) => new(address.GetAddressBytes());

    private static IPAddress? ParseAddress(string text)
    {
        // With several IP headers (a tunnel) TShark is asked for the first one only, but stay safe.
        var first = text.Split(',')[0];

        return IPAddress.TryParse(first, out var address) ? WithoutScope(address) : null;
    }

    private static int? ParseNumber(string text)
    {
        var first = text.Split(',')[0];

        return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}

/// <summary>
/// Sums captured packets per conversation: transport, remote address and service port. A conversation is seen from
/// this device, so every packet is either sent or received.
/// </summary>
public sealed class TrafficAggregator
{
    /// <summary>
    /// Limit of conversations kept between two reports, so that a flood of addresses cannot exhaust memory.
    /// Packets of further conversations are only counted as dropped.
    /// </summary>
    public const int MaxConversations = 5000;

    private static readonly HashSet<string> GenericProtocols = new(StringComparer.OrdinalIgnoreCase) { "TCP", "UDP", "ICMP", "ICMPV6", "OTHER" };

    private readonly object _lock = new();
    private readonly HashSet<IPAddress> _localAddresses;
    private Dictionary<(TrafficTransport Transport, IPAddress Remote, int? Port), Conversation> _conversations = [];
    private DateTimeOffset _periodStart;

    /// <param name="localAddresses">Addresses of this device; they tell whether a packet was sent or received.</param>
    public TrafficAggregator(IEnumerable<IPAddress> localAddresses, DateTimeOffset startedAt)
    {
        _localAddresses = localAddresses.Select(CapturedPacket.WithoutScope).ToHashSet();
        _periodStart = startedAt;
    }

    /// <summary>Packets of conversations that did not fit under <see cref="MaxConversations"/> since the last summary.</summary>
    public long DroppedPackets { get; private set; }

    public void Add(CapturedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // A packet from this device is sent; anything else, including broadcasts from others, is received.
        var sent = _localAddresses.Contains(packet.Source);
        var remote = sent ? packet.Destination : packet.Source;

        // The lower port is usually the service (443, 53); the other one changes with every connection.
        int? servicePort = packet is { SourcePort: { } a, DestinationPort: { } b } ? Math.Min(a, b) : null;

        lock (_lock)
        {
            var key = (packet.Transport, remote, servicePort);

            if (!_conversations.TryGetValue(key, out var conversation))
            {
                if (_conversations.Count >= MaxConversations)
                {
                    DroppedPackets++;

                    return;
                }

                conversation = new Conversation();
                _conversations[key] = conversation;
            }

            conversation.Add(sent, packet.Length, packet.Protocol);
        }
    }

    /// <summary>Returns what was counted since the previous call and starts counting again from zero.</summary>
    public TrafficSummary TakeSummary(DateTimeOffset now)
    {
        Dictionary<(TrafficTransport Transport, IPAddress Remote, int? Port), Conversation> taken;
        DateTimeOffset periodStart;

        lock (_lock)
        {
            taken = _conversations;
            periodStart = _periodStart;
            _conversations = [];
            _periodStart = now;
            DroppedPackets = 0;
        }

        var summary = new TrafficSummary
        {
            PeriodStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(periodStart),
            PeriodEnd = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now),
        };

        summary.Conversations.AddRange(taken.Select(entry =>
        {
            var conversation = new TrafficConversation
            {
                Transport = entry.Key.Transport,
                Protocol = entry.Value.MainProtocol(),
                RemoteAddress = entry.Key.Remote.ToString(),
                PacketsSent = entry.Value.PacketsSent,
                PacketsReceived = entry.Value.PacketsReceived,
                BytesSent = entry.Value.BytesSent,
                BytesReceived = entry.Value.BytesReceived,
            };

            if (entry.Key.Port is { } port)
            {
                conversation.ServicePort = (uint)port;
            }

            return conversation;
        }));

        return summary;
    }

    private sealed class Conversation
    {
        private readonly Dictionary<string, ulong> _bytesByProtocol = new(StringComparer.OrdinalIgnoreCase);

        public ulong PacketsSent { get; private set; }

        public ulong PacketsReceived { get; private set; }

        public ulong BytesSent { get; private set; }

        public ulong BytesReceived { get; private set; }

        public void Add(bool sent, int length, string protocol)
        {
            var bytes = (ulong)Math.Max(0, length);

            if (sent)
            {
                PacketsSent++;
                BytesSent += bytes;
            }
            else
            {
                PacketsReceived++;
                BytesReceived += bytes;
            }

            _bytesByProtocol[protocol] = _bytesByProtocol.GetValueOrDefault(protocol) + bytes;
        }

        /// <summary>
        /// The protocol that carried most of the data. A connection starts and ends with plain TCP packets, so a named
        /// protocol such as TLS is preferred to the bare transport whenever there is one.
        /// </summary>
        public string MainProtocol()
        {
            var named = _bytesByProtocol.Where(entry => !GenericProtocols.Contains(entry.Key)).ToList();
            var candidates = named.Count > 0 ? named : _bytesByProtocol.ToList();

            return candidates.MaxBy(entry => entry.Value).Key;
        }
    }
}
