namespace NetworkMonitoringSystem.Tests.Application;

using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class SystemMetricsReportTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public void ToResourceUsage_KeepsPlausibleValues()
    {
        var report = new SystemMetricsReport(
            42.5,
            16 * Gigabyte,
            6 * Gigabyte,
            [new DiskReport("C:\\", 200 * Gigabyte, 50 * Gigabyte)],
            [new NetworkInterfaceReport("Ethernet", "AA:BB:CC:DD:EE:FF", "192.168.50.20", true, 1000, 2000)]);

        var usage = report.ToResourceUsage();

        Assert.Equal(42.5, usage.CpuUsagePercent);
        Assert.Equal(16 * Gigabyte, usage.MemoryTotalBytes);
        Assert.Equal(6 * Gigabyte, usage.MemoryUsedBytes);

        var disk = Assert.Single(usage.Disks);
        Assert.Equal(("C:\\", 200 * Gigabyte, 50 * Gigabyte), (disk.Name, disk.TotalBytes, disk.FreeBytes));

        var networkInterface = Assert.Single(usage.NetworkInterfaces);
        Assert.Equal("Ethernet", networkInterface.Name);
        Assert.Equal("AA:BB:CC:DD:EE:FF", networkInterface.MacAddress);
        Assert.Equal("192.168.50.20", networkInterface.IpAddress);
        Assert.True(networkInterface.IsUp);
        Assert.Equal((1000L, 2000L), (networkInterface.BytesSent, networkInterface.BytesReceived));
    }

    [Theory]
    [InlineData(150.0, 100.0)]
    [InlineData(-5.0, 0.0)]
    public void ToResourceUsage_LimitsCpuToValidRange(double reported, double expected)
    {
        var usage = new SystemMetricsReport(reported, Gigabyte, 0, [], []).ToResourceUsage();

        Assert.Equal(expected, usage.CpuUsagePercent);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToResourceUsage_DropsCpuValueThatIsNotANumber(double reported)
    {
        var usage = new SystemMetricsReport(reported, Gigabyte, 0, [], []).ToResourceUsage();

        Assert.Null(usage.CpuUsagePercent);
    }

    [Fact]
    public void ToResourceUsage_CorrectsImpossibleSizes()
    {
        var report = new SystemMetricsReport(
            null,
            MemoryTotalBytes: Gigabyte,
            MemoryUsedBytes: 5 * Gigabyte,
            [new DiskReport("C:\\", 100, 500), new DiskReport("D:\\", -1, -1)],
            [new NetworkInterfaceReport("Ethernet", null, null, false, -10, -20)]);

        var usage = report.ToResourceUsage();

        Assert.Equal(Gigabyte, usage.MemoryUsedBytes);
        Assert.Equal(100L, usage.Disks[0].FreeBytes);
        Assert.Equal((0L, 0L), (usage.Disks[1].TotalBytes, usage.Disks[1].FreeBytes));
        Assert.Equal((0L, 0L), (usage.NetworkInterfaces[0].BytesSent, usage.NetworkInterfaces[0].BytesReceived));
        Assert.Null(usage.NetworkInterfaces[0].MacAddress);
    }

    [Fact]
    public void ToResourceUsage_ShortensOverlongTexts_DropsUnnamedItems_AndLimitsTheirNumber()
    {
        var longName = new string('x', 5000);
        var disks = Enumerable.Range(0, 500).Select(index => new DiskReport($"disk-{index}", 10, 5)).ToList();
        var report = new SystemMetricsReport(
            null,
            Gigabyte,
            0,
            disks,
            [
                new NetworkInterfaceReport(longName, longName, longName, true, 0, 0),
                new NetworkInterfaceReport("  ", null, null, true, 0, 0),
            ]);

        var usage = report.ToResourceUsage();

        Assert.Equal(ResourceUsage.MaxItems, usage.Disks.Count);

        var networkInterface = Assert.Single(usage.NetworkInterfaces);
        Assert.Equal(ResourceUsage.MaxNameLength, networkInterface.Name.Length);
        Assert.Equal(ResourceUsage.MaxAddressLength, networkInterface.MacAddress!.Length);
        Assert.Equal(ResourceUsage.MaxAddressLength, networkInterface.IpAddress!.Length);
    }

    [Fact]
    public async Task Heartbeat_WithMetrics_StoresThemInSnapshot_AndWithoutMetricsStoresPlainSnapshot()
    {
        var devices = new InMemoryDeviceRepository();
        var history = new InMemoryMonitoringHistoryRepository();
        var service = new AgentService(
            devices,
            new FixedSettingsRepository(new MonitoringSettings()),
            history,
            new AvailabilityRecorder(history),
            new DeviceActivityRecorder(new InMemoryDeviceActivityRepository(history), history),
            new CountingUnitOfWork(),
            Options.Create(new AgentEnrollmentOptions { EnrollmentToken = "token" }),
            new MutableTimeProvider(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)));
        var registration = await service.RegisterAsync(new RegisterAgentRequest("token", "PC-01", null));

        await service.ReportHeartbeatAsync(
            registration.Credentials,
            new SystemMetricsReport(30, 8 * Gigabyte, 2 * Gigabyte, [new DiskReport("C:\\", 100, 40)], []));
        await service.ReportHeartbeatAsync(registration.Credentials);

        Assert.Equal(3, history.Snapshots.Count);
        Assert.False(history.Snapshots[0].HasResources);

        var measured = history.Snapshots[1];
        Assert.True(measured.HasResources);
        Assert.Equal(30.0, measured.CpuUsagePercent);
        Assert.Equal(8 * Gigabyte, measured.MemoryTotalBytes);
        Assert.Equal(2 * Gigabyte, measured.MemoryUsedBytes);
        Assert.Equal("C:\\", Assert.Single(measured.Disks).Name);

        Assert.False(history.Snapshots[2].HasResources);
        Assert.Null(history.Snapshots[2].CpuUsagePercent);

        var latest = await history.GetLatestResourceSnapshotAsync(registration.Credentials.DeviceId);
        Assert.Same(measured, latest);
    }
}
