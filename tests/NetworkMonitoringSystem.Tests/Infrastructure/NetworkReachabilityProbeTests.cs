namespace NetworkMonitoringSystem.Tests.Infrastructure;

using System.Net;
using System.Net.Sockets;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Monitoring;

public class NetworkReachabilityProbeTests
{
    // 192.0.2.0/24 is reserved for documentation (RFC 5737); no device has such an address.
    private static readonly IPAddress NowhereAddress = IPAddress.Parse("192.0.2.1");

    private readonly NetworkReachabilityProbe _probe = new();

    [Fact]
    public async Task ProbeAsync_ReportsThisComputerAsReachable_ByPing()
    {
        var result = await _probe.ProbeAsync(IPAddress.Loopback, tcpPort: null, TimeSpan.FromSeconds(2));

        Assert.True(result.IsReachable);
        Assert.NotNull(result.ResponseTimeMs);
        Assert.Equal(ReachabilityCheck.Ping, result.ReachedBy);
    }

    [Fact]
    public async Task ProbeAsync_ReportsAddressThatNeverAnswersAsUnreachable()
    {
        var result = await _probe.ProbeAsync(NowhereAddress, tcpPort: null, TimeSpan.FromMilliseconds(500));

        Assert.False(result.IsReachable);
        Assert.Null(result.ResponseTimeMs);
        Assert.Null(result.ReachedBy);
    }

    [Fact]
    public async Task ProbeAsync_TriesTcpPort_AndGivesUpAfterTimeout_WhenNothingAnswers()
    {
        var started = DateTimeOffset.UtcNow;

        var result = await _probe.ProbeAsync(NowhereAddress, tcpPort: 443, TimeSpan.FromMilliseconds(500));

        Assert.False(result.IsReachable);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProbeAsync_PrefersPing_EvenWhenTcpPortIsGiven()
    {
        using var listener = StartListener(out var openPort);

        var result = await _probe.ProbeAsync(IPAddress.Loopback, openPort, TimeSpan.FromSeconds(2));

        Assert.Equal(ReachabilityCheck.Ping, result.ReachedBy);
    }

    [Fact]
    public async Task ConnectToPortAsync_ReportsOpenPortAsReachableByTcp()
    {
        using var listener = StartListener(out var openPort);

        var result = await NetworkReachabilityProbe.ConnectToPortAsync(IPAddress.Loopback, openPort, TimeSpan.FromSeconds(2));

        Assert.True(result.IsReachable);
        Assert.NotNull(result.ResponseTimeMs);
        Assert.Equal(ReachabilityCheck.TcpPort, result.ReachedBy);
    }

    [Fact]
    public async Task ConnectToPortAsync_ReportsClosedPortAsUnreachable()
    {
        int closedPort;

        using (StartListener(out closedPort))
        {
        }

        var result = await NetworkReachabilityProbe.ConnectToPortAsync(IPAddress.Loopback, closedPort, TimeSpan.FromSeconds(2));

        Assert.False(result.IsReachable);
    }

    /// <summary>Listens on a free port of this computer; disposing the result stops listening.</summary>
    private static IDisposable StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;

        return listener;
    }
}
