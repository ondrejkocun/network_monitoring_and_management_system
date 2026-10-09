namespace NetworkMonitoringSystem.Tests;

using NetworkMonitoringSystem.Domain.Devices;

public class UnitTest1
{
    [Fact]
    public void Device_TrimsNameAndHostName()
    {
        var device = new Device(Guid.NewGuid(), "  Server 01  ", "  server-01.local  ");

        Assert.Equal("Server 01", device.Name);
        Assert.Equal("server-01.local", device.HostName);
    }
}
