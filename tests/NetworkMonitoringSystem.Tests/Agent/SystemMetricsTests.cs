namespace NetworkMonitoringSystem.Tests.Agent;

using NetworkMonitoringSystem.Agent.Metrics;

public class SystemMetricsTests
{
    [Theory]
    [InlineData(50UL, 100UL, 0UL, 50.0)]   // half of the time idle
    [InlineData(100UL, 100UL, 0UL, 0.0)]   // idle all the time
    [InlineData(0UL, 60UL, 40UL, 100.0)]   // never idle
    [InlineData(25UL, 60UL, 40UL, 75.0)]
    public void UsagePercentBetween_ComputesLoadFromIdleShare(ulong idle, ulong kernel, ulong user, double expected)
    {
        var earlier = new CpuTimes(1000, 5000, 3000);
        var later = new CpuTimes(1000 + idle, 5000 + kernel, 3000 + user);

        var usage = CpuTimes.UsagePercentBetween(earlier, later);

        Assert.NotNull(usage);
        Assert.Equal(expected, usage.Value, precision: 6);
    }

    [Fact]
    public void UsagePercentBetween_ReturnsNull_WhenNoTimePassed()
    {
        var times = new CpuTimes(1000, 5000, 3000);

        Assert.Null(CpuTimes.UsagePercentBetween(times, times));
    }

    [Fact]
    public void UsagePercentBetween_ReturnsNull_WhenCountersWentBackwards()
    {
        Assert.Null(CpuTimes.UsagePercentBetween(new CpuTimes(1000, 5000, 3000), new CpuTimes(10, 50, 30)));
    }

    [Fact]
    public void CpuUsageMeter_ReturnsNullFirst_ThenLoadSincePreviousCall()
    {
        var readings = new Queue<CpuTimes>(
        [
            new CpuTimes(0, 0, 0),
            new CpuTimes(80, 100, 0),
            new CpuTimes(90, 150, 50),
        ]);
        var meter = new CpuUsageMeter(readings.Dequeue);

        Assert.Null(meter.Measure());
        Assert.Equal(20.0, meter.Measure()!.Value, precision: 6);
        Assert.Equal(90.0, meter.Measure()!.Value, precision: 6);
    }

    [Theory]
    [InlineData("Ethernet", false)]
    [InlineData("Wi-Fi", false)]
    [InlineData("Ethernet 2", false)]
    [InlineData("Ethernet-QoS Packet Scheduler-0000", true)]
    [InlineData("Ethernet-Npcap Packet Driver (NPCAP)-0000", true)]
    [InlineData("Wi-Fi-WFP Native MAC Layer LightWeight Filter-0000", true)]
    [InlineData("Lab-1234", false)]
    public void IsDriverLayerOf_RecognizesFilterDriversBoundToAnAdapter(string name, bool expected)
    {
        string[] allNames =
        [
            "Ethernet", "Wi-Fi", "Ethernet 2", "Lab-1234",
            "Ethernet-QoS Packet Scheduler-0000",
            "Ethernet-Npcap Packet Driver (NPCAP)-0000",
            "Wi-Fi-WFP Native MAC Layer LightWeight Filter-0000",
        ];

        Assert.Equal(expected, WindowsSystemMetricsCollector.IsDriverLayerOf(name, allNames));
    }

    [Fact]
    public async Task WindowsCollector_MeasuresThisComputer()
    {
        var collector = new WindowsSystemMetricsCollector();
        await Task.Delay(200);

        var metrics = collector.Collect();

        Assert.True(metrics.HasCpuUsagePercent);
        Assert.InRange(metrics.CpuUsagePercent, 0, 100);

        Assert.True(metrics.MemoryTotalBytes > 1024UL * 1024 * 1024, "A computer running the tests has more than 1 GB of memory.");
        Assert.InRange(metrics.MemoryUsedBytes, 1UL, metrics.MemoryTotalBytes);

        Assert.NotEmpty(metrics.Disks);
        Assert.All(metrics.Disks, disk =>
        {
            Assert.False(string.IsNullOrWhiteSpace(disk.Name));
            Assert.True(disk.TotalBytes > 0);
            Assert.True(disk.FreeBytes <= disk.TotalBytes);
        });

        Assert.NotEmpty(metrics.NetworkInterfaces);
        Assert.All(metrics.NetworkInterfaces, networkInterface => Assert.False(string.IsNullOrWhiteSpace(networkInterface.Name)));
        Assert.DoesNotContain(metrics.NetworkInterfaces, networkInterface => networkInterface.Name.EndsWith("-0000", StringComparison.Ordinal));
    }
}
