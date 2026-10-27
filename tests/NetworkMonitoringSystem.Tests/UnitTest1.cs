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

    [Fact]
    public void Device_WithAgent_TakesTheAddressItsAgentReports_IgnoringLoopbackAndUnspecified()
    {
        var device = new Device(Guid.NewGuid(), "PC", MonitoringMode.Agent, "pc-01", null, CreatedAt);

        Assert.False(device.RecordNetworkAddress(null));
        Assert.False(device.RecordNetworkAddress(IPAddress.Loopback));
        Assert.False(device.RecordNetworkAddress(IPAddress.IPv6Loopback));
        Assert.False(device.RecordNetworkAddress(IPAddress.Any));
        Assert.Null(device.IpAddress);

        Assert.True(device.RecordNetworkAddress(IPAddress.Parse("192.168.50.20")));
        Assert.False(device.RecordNetworkAddress(IPAddress.Parse("192.168.50.20")));
        Assert.True(device.RecordNetworkAddress(IPAddress.Parse("10.0.0.7")));
        Assert.Equal(IPAddress.Parse("10.0.0.7"), device.IpAddress);
    }

    [Fact]
    public void Device_CanBeRenamed_ButNotToAnEmptyOrTooLongName()
    {
        var device = new Device(Guid.NewGuid(), "DESKTOP-TKKTAJ2", MonitoringMode.Agent, "DESKTOP-TKKTAJ2", null, CreatedAt);

        Assert.True(device.Rename("  Učebňa 3  "));
        Assert.False(device.Rename("Učebňa 3"));
        Assert.Throws<ArgumentException>(() => device.Rename(""));
        Assert.Throws<ArgumentException>(() => device.Rename(new string('x', Device.MaxNameLength + 1)));
        Assert.Throws<ArgumentException>(() => new Device(Guid.NewGuid(), new string('x', Device.MaxNameLength + 1), MonitoringMode.Agent, "pc", null, CreatedAt));

        Assert.Equal(("Učebňa 3", "DESKTOP-TKKTAJ2"), (device.Name, device.HostName));
    }

    [Fact]
    public void Device_WithoutAgent_KeepsTheAddressItIsCheckedAt()
    {
        var device = new Device(Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, IPAddress.Parse("192.168.50.1"), CreatedAt);

        Assert.False(device.RecordNetworkAddress(IPAddress.Parse("192.168.50.99")));
        Assert.Equal(IPAddress.Parse("192.168.50.1"), device.IpAddress);
    }
}
