namespace NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>Transport of captured traffic. Unlike ports, traffic also includes ICMP and other IP protocols.</summary>
public enum TrafficTransport
{
    Tcp = 0,
    Udp = 1,
    Icmp = 2,
    Other = 3,
}

/// <summary>
/// How much a device communicated with one remote address over one protocol and service during a period.
/// Only the summary is stored, never the contents of the packets.
/// </summary>
public sealed class TrafficRecord
{
    public const int MaxProtocolLength = 32;

    public TrafficRecord(
        Guid deviceId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        TrafficTransport transport,
        string protocol,
        string remoteAddress,
        int? servicePort,
        long packetsSent,
        long packetsReceived,
        long bytesSent,
        long bytesReceived)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        if (periodEnd < periodStart)
        {
            throw new ArgumentOutOfRangeException(nameof(periodEnd), periodEnd, "A period cannot end before it started.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteAddress);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(protocol.Length, MaxProtocolLength, nameof(protocol));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(remoteAddress.Length, ListeningPort.MaxAddressLength, nameof(remoteAddress));

        if (servicePort is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(servicePort), servicePort, "Port must be between 0 and 65535.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(packetsSent);
        ArgumentOutOfRangeException.ThrowIfNegative(packetsReceived);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesSent);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesReceived);

        DeviceId = deviceId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Transport = transport;
        Protocol = protocol;
        RemoteAddress = remoteAddress;
        ServicePort = servicePort;
        PacketsSent = packetsSent;
        PacketsReceived = packetsReceived;
        BytesSent = bytesSent;
        BytesReceived = bytesReceived;
    }

    private TrafficRecord()
    {
        Protocol = null!;
        RemoteAddress = null!;
    }

    public long Id { get; private set; }

    public Guid DeviceId { get; private set; }

    public DateTimeOffset PeriodStart { get; private set; }

    public DateTimeOffset PeriodEnd { get; private set; }

    public TrafficTransport Transport { get; private set; }

    /// <summary>The highest protocol recognised in the packets, for example TLS, DNS or HTTP, or just TCP or UDP.</summary>
    public string Protocol { get; private set; }

    public string RemoteAddress { get; private set; }

    /// <summary>
    /// The lower of the two ports, which is usually the service (443 for HTTPS, 53 for DNS);
    /// null for traffic without ports, such as ICMP.
    /// </summary>
    public int? ServicePort { get; private set; }

    public long PacketsSent { get; private set; }

    public long PacketsReceived { get; private set; }

    public long BytesSent { get; private set; }

    public long BytesReceived { get; private set; }
}
