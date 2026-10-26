namespace NetworkMonitoringSystem.Tests;

using System.Net;
using NetworkMonitoringSystem.Domain.Devices;

public class UnitTest1
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Device_TrimsNameAndHostName()
    {
        var device = new Device(Guid.NewGuid(), "  Server 01  ", MonitoringMode.Agent, "  server-01.local  ", null, CreatedAt);

        Assert.Equal("Server 01", device.Name);
        Assert.Equal("server-01.local", device.HostName);
    }

    [Fact]
    public void Device_StartsEnabledWithUnknownStatus()
    {
        var device = new Device(Guid.NewGuid(), "Server 01", MonitoringMode.Agent, "server-01.local", null, CreatedAt);

        Assert.Equal(DeviceStatus.Unknown, device.Status);
        Assert.Null(device.LastSeenAt);
        Assert.True(device.IsEnabled);
        Assert.Equal(CreatedAt, device.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Device_WithAgent_RequiresHostName(string? hostName)
    {
        Assert.Throws<ArgumentException>(
            () => new Device(Guid.NewGuid(), "Server 01", MonitoringMode.Agent, hostName, IPAddress.Parse("192.168.50.10"), CreatedAt));
    }

    [Fact]
    public void Device_WithoutAgent_RequiresIpAddress()
    {
        Assert.Throws<ArgumentException>(
            () => new Device(Guid.NewGuid(), "Router", MonitoringMode.Agentless, "router.local", null, CreatedAt));
    }

    [Fact]
    public void Device_WithoutAgent_DoesNotRequireHostName()
    {
        var device = new Device(Guid.NewGuid(), "Router", MonitoringMode.Agentless, " ", IPAddress.Parse("192.168.50.1"), CreatedAt);

        Assert.Null(device.HostName);
        Assert.Equal(IPAddress.Parse("192.168.50.1"), device.IpAddress);
    }

    [Fact]
    public void Device_WithoutAgent_CanHaveTcpPort_AndLoseItAgain()
    {
        var device = new Device(Guid.NewGuid(), "NAS", MonitoringMode.Agentless, null, IPAddress.Parse("192.168.50.5"), CreatedAt);
        Assert.Null(device.TcpPort);

        device.SetTcpPort(445);
        Assert.Equal(445, device.TcpPort);

        device.SetTcpPort(null);
        Assert.Null(device.TcpPort);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Device_RejectsTcpPortOutOfRange(int port)
    {
        var device = new Device(Guid.NewGuid(), "NAS", MonitoringMode.Agentless, null, IPAddress.Parse("192.168.50.5"), CreatedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => device.SetTcpPort(port));
    }

    [Fact]
    public void Device_WithAgent_CannotHaveTcpPort()
    {
        var device = new Device(Guid.NewGuid(), "PC", MonitoringMode.Agent, "pc-01", null, CreatedAt);

        Assert.Throws<InvalidOperationException>(() => device.SetTcpPort(3389));
    }
}
