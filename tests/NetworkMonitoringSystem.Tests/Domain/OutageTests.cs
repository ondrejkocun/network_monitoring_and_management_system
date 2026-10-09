namespace NetworkMonitoringSystem.Tests.Domain;

using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

public class OutageTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewOutage_IsOngoing()
    {
        var outage = new Outage(Guid.NewGuid(), Start);

        Assert.True(outage.IsOngoing);
        Assert.Null(outage.EndedAt);
    }

    [Fact]
    public void End_SetsEndTime()
    {
        var outage = new Outage(Guid.NewGuid(), Start);

        outage.End(Start.AddMinutes(5));

        Assert.False(outage.IsOngoing);
        Assert.Equal(Start.AddMinutes(5), outage.EndedAt);
    }

    [Fact]
    public void End_Throws_WhenEndIsBeforeStart()
    {
        var outage = new Outage(Guid.NewGuid(), Start);

        Assert.Throws<ArgumentOutOfRangeException>(() => outage.End(Start.AddSeconds(-1)));
        Assert.True(outage.IsOngoing);
    }

    [Fact]
    public void End_Throws_WhenAlreadyEnded()
    {
        var outage = new Outage(Guid.NewGuid(), Start);
        outage.End(Start.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => outage.End(Start.AddMinutes(6)));
    }

    [Fact]
    public void Device_ReportsOnlyRealStatusChanges()
    {
        var device = new Device(Guid.NewGuid(), "PC-01", MonitoringMode.Agent, "pc-01", null, Start);

        Assert.True(device.RecordContact(Start));
        Assert.False(device.RecordContact(Start.AddMinutes(1)));
        Assert.True(device.MarkOffline());
        Assert.False(device.MarkOffline());
        Assert.True(device.RecordContact(Start.AddMinutes(2)));
        Assert.Equal(Start.AddMinutes(2), device.LastSeenAt);
    }
}
