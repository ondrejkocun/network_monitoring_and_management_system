namespace NetworkMonitoringSystem.Tests.Application;

using System.Net;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

public class TrafficReportTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeviceId = Guid.NewGuid();

    [Fact]
    public void ToRecords_PlacesPeriodByServerClock_KeepingItsLength()
    {
        // The agent's clock is a year behind.
        var agentEnd = ReceivedAt.AddYears(-1);
        var report = new TrafficReport(agentEnd.AddSeconds(-60), agentEnd, [Conversation("TLS", 1000)]);

        var record = Assert.Single(report.ToRecords(DeviceId, ReceivedAt));

        Assert.Equal(ReceivedAt.AddSeconds(-60), record.PeriodStart);
        Assert.Equal(ReceivedAt, record.PeriodEnd);
    }

    [Theory]
    [InlineData(-60, 0)]
    [InlineData(3 * 24 * 3600, 24 * 3600)]
    public void ToRecords_LimitsImplausiblePeriods(int reportedSeconds, int storedSeconds)
    {
        var report = new TrafficReport(ReceivedAt, ReceivedAt.AddSeconds(reportedSeconds), [Conversation("TLS", 1000)]);

        var record = Assert.Single(report.ToRecords(DeviceId, ReceivedAt));

        Assert.Equal(TimeSpan.FromSeconds(storedSeconds), record.PeriodEnd - record.PeriodStart);
    }

    [Fact]
    public void ToRecords_KeepsOnlyTheLargestConversations()
    {
        var conversations = Enumerable.Range(1, TrafficReport.MaxConversations + 50)
            .Select(bytes => Conversation("TLS", bytes))
            .ToList();

        var records = new TrafficReport(ReceivedAt, ReceivedAt, conversations).ToRecords(DeviceId, ReceivedAt);

        Assert.Equal(TrafficReport.MaxConversations, records.Count);
        Assert.Equal(51, records.Min(record => record.BytesSent));
    }

    [Fact]
    public void ToRecords_LeavesOutWhatCannotBeStored_AndShortensProtocol()
    {
        var report = new TrafficReport(ReceivedAt, ReceivedAt,
        [
            Conversation(" ", 10),
            Conversation("TLS", 10) with { RemoteAddress = new string('a', ListeningPort.MaxAddressLength + 1) },
            Conversation("TLS", 10) with { PacketsSent = 0, PacketsReceived = 0 },
            Conversation("TLS", 10) with { ServicePort = 70000, BytesReceived = -5 },
            Conversation(new string('P', 100), 10),
        ]);

        var records = report.ToRecords(DeviceId, ReceivedAt);

        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => record.ServicePort is null && record.BytesReceived == 0);
        Assert.Contains(records, record => record.Protocol.Length == TrafficRecord.MaxProtocolLength);
    }

    [Fact]
    public void Device_CaptureTraffic_OnlyWithAgent_AndSwitchingClearsProblem()
    {
        var agentDevice = new Device(Guid.NewGuid(), "PC", MonitoringMode.Agent, "pc", null, ReceivedAt);
        var router = new Device(Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, IPAddress.Loopback, ReceivedAt);

        Assert.True(agentDevice.SetTrafficCapture(true));
        Assert.False(agentDevice.SetTrafficCapture(true));
        agentDevice.RecordTrafficCaptureProblem(new string('x', Device.MaxTrafficCaptureProblemLength + 10));
        Assert.Equal(Device.MaxTrafficCaptureProblemLength, agentDevice.TrafficCaptureProblem!.Length);

        Assert.True(agentDevice.SetTrafficCapture(false));
        Assert.Null(agentDevice.TrafficCaptureProblem);
        Assert.Throws<InvalidOperationException>(() => router.SetTrafficCapture(true));
    }

    private static ConversationReport Conversation(string protocol, long bytes)
    {
        return new ConversationReport(TrafficTransport.Tcp, protocol, "192.0.2.10", 443, 1, 1, bytes, 0);
    }
}
