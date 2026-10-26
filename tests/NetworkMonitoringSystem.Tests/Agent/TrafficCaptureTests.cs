namespace NetworkMonitoringSystem.Tests.Agent;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Traffic;
using NetworkMonitoringSystem.Contracts.Agents;

public class TrafficCaptureTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly IPAddress Local = IPAddress.Parse("192.168.50.10");

    [Fact]
    public void TryParse_ReadsIPv4TcpPacket()
    {
        var packet = CapturedPacket.TryParse(Line("1514", "192.168.50.10", "160.79.104.10", "", "", "6", "", "57764", "443", "", "", "TLSv1.3"));

        Assert.Equal(
            new CapturedPacket(1514, Local, IPAddress.Parse("160.79.104.10"), TrafficTransport.Tcp, 57764, 443, "TLSv1.3"),
            packet);
    }

    [Fact]
    public void TryParse_ReadsIPv6IcmpPacket_WithoutPorts()
    {
        var packet = CapturedPacket.TryParse(Line("86", "", "", "fe80::1", "ff02::1:ff5d:fa68", "", "58", "", "", "", "", "ICMPv6"));

        Assert.NotNull(packet);
        Assert.Equal(TrafficTransport.Icmp, packet.Transport);
        Assert.Null(packet.SourcePort);
        Assert.Equal(IPAddress.Parse("fe80::1"), packet.Source);
    }

    [Theory]
    [InlineData("60\t\t\t\t\t\t\t\t\t\t\tARP")]
    [InlineData("")]
    [InlineData("not a packet")]
    [InlineData("60\t192.168.50.10")]
    public void TryParse_SkipsWhatIsNotIPTraffic(string line)
    {
        Assert.Null(CapturedPacket.TryParse(line));
    }

    [Fact]
    public void TryParse_TakesTransportNameWhenTsharkGivesNoProtocol_AndDropsEscapes()
    {
        Assert.Equal("UDP", CapturedPacket.TryParse(Line("62", "192.168.50.10", "192.168.50.255", "", "", "17", "", "", "", "2008", "2008", ""))!.Protocol);
        Assert.Equal("TCP", CapturedPacket.TryParse(Line("66", "192.168.50.10", "1.1.1.1", "", "", "6", "", "1", "2", "", "", "\\TCP"))!.Protocol);
    }

    [Fact]
    public void Aggregator_CountsSentAndReceived_PerRemoteAndServicePort()
    {
        var aggregator = new TrafficAggregator([Local], Start);
        var server = IPAddress.Parse("160.79.104.10");

        // Two connections from different local ports to the same service are one conversation.
        aggregator.Add(new CapturedPacket(100, Local, server, TrafficTransport.Tcp, 57764, 443, "TCP"));
        aggregator.Add(new CapturedPacket(1500, server, Local, TrafficTransport.Tcp, 443, 57764, "TLSv1.3"));
        aggregator.Add(new CapturedPacket(200, Local, server, TrafficTransport.Tcp, 61000, 443, "TLSv1.3"));
        aggregator.Add(new CapturedPacket(80, Local, IPAddress.Parse("192.168.50.1"), TrafficTransport.Udp, 50000, 53, "DNS"));

        var summary = aggregator.TakeSummary(Start.AddSeconds(60));

        Assert.Equal(2, summary.Conversations.Count);
        var tls = Assert.Single(summary.Conversations, conversation => conversation.ServicePort == 443);
        Assert.Equal(("TLSv1.3", "160.79.104.10", TrafficTransport.Tcp), (tls.Protocol, tls.RemoteAddress, tls.Transport));
        Assert.Equal((2UL, 1UL, 300UL, 1500UL), (tls.PacketsSent, tls.PacketsReceived, tls.BytesSent, tls.BytesReceived));
        Assert.Equal(Start, summary.PeriodStart.ToDateTimeOffset());
        Assert.Equal(Start.AddSeconds(60), summary.PeriodEnd.ToDateTimeOffset());
    }

    [Fact]
    public void Aggregator_StartsAgainAfterSummary()
    {
        var aggregator = new TrafficAggregator([Local], Start);
        aggregator.Add(new CapturedPacket(100, Local, IPAddress.Parse("1.1.1.1"), TrafficTransport.Icmp, null, null, "ICMP"));

        Assert.Single(aggregator.TakeSummary(Start.AddSeconds(60)).Conversations);
        var next = aggregator.TakeSummary(Start.AddSeconds(120));

        Assert.Empty(next.Conversations);
        Assert.Equal(Start.AddSeconds(60), next.PeriodStart.ToDateTimeOffset());
    }

    [Fact]
    public void Aggregator_CountsAnnouncementsOfOtherDevicesPerGroup_NotPerSender()
    {
        var aggregator = new TrafficAggregator([Local], Start, [IPAddress.Parse("192.168.50.255")]);

        // Many neighbours announce themselves over SSDP, mDNS and NetBIOS broadcasts.
        for (var host = 20; host < 120; host++)
        {
            var neighbour = IPAddress.Parse($"192.168.50.{host}");
            aggregator.Add(new CapturedPacket(179, neighbour, IPAddress.Parse("239.255.255.250"), TrafficTransport.Udp, 50000 + host, 1900, "SSDP"));
            aggregator.Add(new CapturedPacket(90, neighbour, IPAddress.Parse("ff02::fb"), TrafficTransport.Udp, 5353, 5353, "MDNS"));
            aggregator.Add(new CapturedPacket(92, neighbour, IPAddress.Parse("192.168.50.255"), TrafficTransport.Udp, 137, 137, "NBNS"));
        }

        // A packet addressed to this device alone is still counted per sender.
        aggregator.Add(new CapturedPacket(60, IPAddress.Parse("192.168.50.20"), Local, TrafficTransport.Icmp, null, null, "ICMP"));

        var conversations = aggregator.TakeSummary(Start).Conversations;

        Assert.Equal(
            ["192.168.50.20", "192.168.50.255", "239.255.255.250", "ff02::fb"],
            conversations.Select(conversation => conversation.RemoteAddress).Order());
        var ssdp = Assert.Single(conversations, conversation => conversation.Protocol == "SSDP");
        Assert.Equal((100UL, 0UL), (ssdp.PacketsReceived, ssdp.PacketsSent));
    }

    [Fact]
    public void Aggregator_KeepsAtMostTheLimitOfConversations()
    {
        var aggregator = new TrafficAggregator([Local], Start);

        for (var port = 1; port <= TrafficAggregator.MaxConversations + 10; port++)
        {
            aggregator.Add(new CapturedPacket(60, Local, IPAddress.Parse("1.1.1.1"), TrafficTransport.Udp, 60000, port, "UDP"));
        }

        Assert.Equal(10, aggregator.DroppedPackets);
        Assert.Equal(TrafficAggregator.MaxConversations, aggregator.TakeSummary(Start).Conversations.Count);
    }

    [Fact]
    public void SelectInterfaces_TakesOnlyNpcapDevicesOfActiveAdapters()
    {
        var active = NetworkInterface.GetAllNetworkInterfaces().First(adapter =>
            adapter.OperationalStatus == OperationalStatus.Up
            && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            && adapter.GetIPProperties().UnicastAddresses.Count > 0);
        var list = $"""
            1. \Device\NPF_{active.Id} (Ethernet)
            2. \Device\NPF_{"{00000000-0000-0000-0000-000000000000}"} (Gone)
            3. \Device\NPF_Loopback (Adapter for loopback traffic capture)
            4. sshdump (SSH remote capture)
            """;

        var selected = TsharkTrafficCapture.SelectInterfaces(list, NetworkInterface.GetAllNetworkInterfaces());

        Assert.Equal([$@"\Device\NPF_{active.Id}"], selected);
    }

    [Fact]
    public void Capture_WithoutTshark_ReportsWhy_AndHasNoConversations()
    {
        using var capture = CreateCapture(new AgentOptions { TsharkPath = @"C:\nowhere\tshark.exe" });

        capture.SetEnabled(true);
        var summary = capture.TakeSummary();

        Assert.True(capture.IsEnabled);
        Assert.Contains("TShark was not found", summary.CaptureProblem);
        Assert.Empty(summary.Conversations);
    }

    /// <summary>Runs the real TShark on the loopback adapter of Npcap. Passes without checking when TShark is not installed.</summary>
    [Fact]
    public async Task Capture_WithRealTshark_CountsTrafficOnThisComputer()
    {
        using var capture = CreateCapture(new AgentOptions { CaptureInterfaces = [@"\Device\NPF_Loopback"] });

        if (!File.Exists(capture.TsharkPath))
        {
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var payload = new byte[5000];

        capture.SetEnabled(true);
        capture.TakeSummary();

        var counted = 0UL;
        var deadline = DateTime.UtcNow.AddSeconds(30);

        // TShark needs a moment before it captures, so traffic is sent until it shows up.
        while (counted < (ulong)payload.Length && DateTime.UtcNow < deadline)
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                using var accepted = await listener.AcceptTcpClientAsync();
                await client.GetStream().WriteAsync(payload);
                await accepted.GetStream().ReadExactlyAsync(new byte[payload.Length]);
            }

            await Task.Delay(1000);

            var summary = capture.TakeSummary();
            Assert.Equal(string.Empty, summary.CaptureProblem);

            counted += summary.Conversations
                .Where(conversation => conversation.Transport == TrafficTransport.Tcp && conversation.RemoteAddress == "127.0.0.1")
                .Aggregate(0UL, (sum, conversation) => sum + conversation.BytesSent + conversation.BytesReceived);
        }

        capture.SetEnabled(false);

        Assert.True(counted >= (ulong)payload.Length, $"Only {counted} bytes were counted.");
    }

    /// <summary>Starts the real TShark on the adapters it chooses itself. Passes without checking when TShark is not installed.</summary>
    [Fact]
    public async Task Capture_WithRealTshark_StartsOnActiveAdaptersOfThisComputer()
    {
        using var capture = CreateCapture(new AgentOptions());

        if (!File.Exists(capture.TsharkPath))
        {
            return;
        }

        capture.SetEnabled(true);
        await Task.Delay(3000);
        var summary = capture.TakeSummary();
        capture.SetEnabled(false);

        Assert.Equal(string.Empty, summary.CaptureProblem);
    }

    private static TsharkTrafficCapture CreateCapture(AgentOptions options)
    {
        return new TsharkTrafficCapture(Options.Create(options), NullLogger<TsharkTrafficCapture>.Instance);
    }

    private static string Line(params string[] fields) => string.Join('\t', fields);
}
