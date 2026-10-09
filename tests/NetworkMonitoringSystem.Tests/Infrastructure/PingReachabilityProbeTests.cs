namespace NetworkMonitoringSystem.Tests.Infrastructure;

using System.Net;
using NetworkMonitoringSystem.Infrastructure.Monitoring;

public class PingReachabilityProbeTests
{
    private readonly PingReachabilityProbe _probe = new();

    [Fact]
    public async Task ProbeAsync_ReportsThisComputerAsReachable()
    {
        var result = await _probe.ProbeAsync(IPAddress.Loopback, TimeSpan.FromSeconds(2));

        Assert.True(result.IsReachable);
        Assert.NotNull(result.ResponseTimeMs);
    }

    [Fact]
    public async Task ProbeAsync_ReportsAddressThatNeverAnswersAsUnreachable()
    {
        // 192.0.2.0/24 is reserved for documentation (RFC 5737); no device has such an address.
        var result = await _probe.ProbeAsync(IPAddress.Parse("192.0.2.1"), TimeSpan.FromMilliseconds(500));

        Assert.False(result.IsReachable);
        Assert.Null(result.ResponseTimeMs);
    }
}
