using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Agents;

/// <summary>
/// Summary of the network traffic an agent captured since its previous report, one entry per conversation.
/// </summary>
/// <param name="PeriodStart">When the agent started counting, by the agent's clock.</param>
/// <param name="PeriodEnd">When the agent stopped counting, by the agent's clock.</param>
/// <param name="CaptureProblem">Why the agent could not capture, or null when capture works.</param>
public sealed record TrafficReport(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    IReadOnlyList<ConversationReport> Conversations,
    string? CaptureProblem = null)
{
    /// <summary>Only the largest conversations of one report are stored, so the history cannot grow without limit.</summary>
    public const int MaxConversations = 200;

    /// <summary>A longer period is not believed; the agent reports at least once per synchronization interval.</summary>
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(1);

    /// <summary>
    /// Converts the report into records to store. The period is placed to end at <paramref name="receivedAt"/> by the
    /// server's clock, so an agent whose clock is wrong cannot put traffic in the past or the future. Entries that
    /// cannot be stored are left out; only the largest <see cref="MaxConversations"/> are kept.
    /// </summary>
    public IReadOnlyList<TrafficRecord> ToRecords(Guid deviceId, DateTimeOffset receivedAt)
    {
        var length = PeriodEnd - PeriodStart;
        length = length < TimeSpan.Zero ? TimeSpan.Zero : length > MaxPeriod ? MaxPeriod : length;
        var periodStart = receivedAt - length;

        return Conversations
            .Where(IsStorable)
            .OrderByDescending(conversation => Clamp(conversation.BytesSent) + Clamp(conversation.BytesReceived))
            .Take(MaxConversations)
            .Select(conversation => new TrafficRecord(
                deviceId,
                periodStart,
                receivedAt,
                conversation.Transport,
                Shorten(conversation.Protocol.Trim(), TrafficRecord.MaxProtocolLength),
                conversation.RemoteAddress.Trim(),
                conversation.ServicePort is >= 0 and <= 65535 ? conversation.ServicePort : null,
                Clamp(conversation.PacketsSent),
                Clamp(conversation.PacketsReceived),
                Clamp(conversation.BytesSent),
                Clamp(conversation.BytesReceived)))
            .ToList();
    }

    private static bool IsStorable(ConversationReport conversation)
    {
        return !string.IsNullOrWhiteSpace(conversation.Protocol)
            && !string.IsNullOrWhiteSpace(conversation.RemoteAddress)
            && conversation.RemoteAddress.Trim().Length <= ListeningPort.MaxAddressLength
            && Enum.IsDefined(conversation.Transport)
            && conversation.PacketsSent + conversation.PacketsReceived > 0;
    }

    private static long Clamp(long value) => Math.Max(0, value);

    private static string Shorten(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];
}

/// <param name="Protocol">The highest protocol recognised in the packets, for example TLS or DNS.</param>
/// <param name="ServicePort">The lower of the two ports; null for traffic without ports.</param>
public sealed record ConversationReport(
    TrafficTransport Transport,
    string Protocol,
    string RemoteAddress,
    int? ServicePort,
    long PacketsSent,
    long PacketsReceived,
    long BytesSent,
    long BytesReceived);
