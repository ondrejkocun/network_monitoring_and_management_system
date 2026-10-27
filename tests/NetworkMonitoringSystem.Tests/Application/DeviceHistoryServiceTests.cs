namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class DeviceHistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly DeviceHistoryService _service;

    public DeviceHistoryServiceTests()
    {
        _service = new DeviceHistoryService(_devices, _history, new MutableTimeProvider(Now));
    }

    [Fact]
    public async Task Availability_IsTheShareOfThePeriodOutsideOutages()
    {
        var device = AddDevice(createdAt: Now.AddDays(-30));
        AddOutage(device, Now.AddHours(-10), Now.AddHours(-9));
        AddOutage(device, Now.AddHours(-3), Now.AddHours(-2).AddMinutes(-30));

        var history = await _service.GetHistoryAsync(device.Id, Now.AddHours(-24), Now);

        Assert.NotNull(history);
        Assert.Equal(TimeSpan.FromMinutes(90), history.Downtime);
        Assert.Equal(100.0 * (24 * 60 - 90) / (24 * 60), history.AvailabilityPercent!.Value, precision: 6);

        // The most recent outage first.
        Assert.Equal([Now.AddHours(-3), Now.AddHours(-10)], history.Outages.Select(outage => outage.StartedAt));
    }

    [Fact]
    public async Task Availability_CountsOnlyThePartOfAnOutageInsideThePeriod_AndAnOngoingOutageUpToNow()
    {
        var device = AddDevice(createdAt: Now.AddDays(-30));
        // Started before the period and ended one hour into it.
        AddOutage(device, Now.AddHours(-30), Now.AddHours(-23));
        // Still in progress.
        AddOutage(device, Now.AddHours(-2), endedAt: null);
        // Entirely before the period.
        AddOutage(device, Now.AddDays(-5), Now.AddDays(-4));

        var history = await _service.GetHistoryAsync(device.Id, Now.AddHours(-24), Now);

        Assert.Equal(TimeSpan.FromHours(3), history!.Downtime);
        Assert.Equal(87.5, history.AvailabilityPercent!.Value, precision: 6);
        Assert.Equal(2, history.Outages.Count);
        Assert.Null(history.Outages[0].EndedAt);
    }

    [Fact]
    public async Task Availability_IsCountedOnlyFromTheMomentTheDeviceWasAdded()
    {
        var device = AddDevice(createdAt: Now.AddHours(-2));
        AddOutage(device, Now.AddHours(-1), Now);

        var history = await _service.GetHistoryAsync(device.Id, Now.AddHours(-24), Now);

        // One hour out of the two the device has existed, not out of twenty-four.
        Assert.Equal(50, history!.AvailabilityPercent!.Value, precision: 6);
    }

    [Fact]
    public async Task Availability_IsUnknown_ForAPeriodBeforeTheDeviceWasAdded()
    {
        var device = AddDevice(createdAt: Now.AddHours(-1));

        var history = await _service.GetHistoryAsync(device.Id, Now.AddDays(-3), Now.AddDays(-2));

        Assert.Null(history!.AvailabilityPercent);
        Assert.Equal(TimeSpan.Zero, history.Downtime);
    }

    [Fact]
    public async Task Availability_IsFull_WhenThereWasNoOutage()
    {
        var device = AddDevice(createdAt: Now.AddDays(-30));

        var history = await _service.GetHistoryAsync(device.Id, Now.AddHours(-1), Now);

        Assert.Equal(100, history!.AvailabilityPercent);
        Assert.Empty(history.Outages);
    }

    [Fact]
    public async Task Events_AreThoseOfTheDeviceInThePeriod_MostRecentFirst()
    {
        var device = AddDevice(createdAt: Now.AddDays(-30));
        var other = AddDevice(createdAt: Now.AddDays(-30));
        AddEvent(Now.AddHours(-3), "older", device.Id);
        AddEvent(Now.AddHours(-1), "newer", device.Id);
        AddEvent(Now.AddDays(-2), "before the period", device.Id);
        AddEvent(Now.AddHours(-1), "another device", other.Id);
        AddEvent(Now.AddHours(-1), "whole system", deviceId: null);

        var history = await _service.GetHistoryAsync(device.Id, Now.AddHours(-24), Now);

        Assert.Equal(["newer", "older"], history!.Events.Select(monitoringEvent => monitoringEvent.Message));
        Assert.False(history.EventsTruncated);
    }

    [Fact]
    public async Task Events_AreLimited_AndTheReplySaysThereAreMore()
    {
        var device = AddDevice(createdAt: Now.AddDays(-30));

        for (var index = 0; index <= DeviceHistoryService.MaxEvents; index++)
        {
            AddEvent(Now.AddSeconds(-index), $"event {index}", device.Id);
        }

        var history = await _service.GetHistoryAsync(device.Id, Now.AddHours(-1), Now);

        Assert.Equal(DeviceHistoryService.MaxEvents, history!.Events.Count);
        Assert.True(history.EventsTruncated);
        Assert.Equal("event 0", history.Events[0].Message);
    }

    [Fact]
    public async Task NetworkPoints_GiveTrafficPerSecondBetweenReports_AndTheResponseTimeTheServerMeasured()
    {
        var device = AddDevice(createdAt: Now.AddDays(-30));
        AddSnapshot(device, Now.AddMinutes(-2), ("Ethernet", 1_000, 10_000));
        AddSnapshot(device, Now.AddMinutes(-1), ("Ethernet", 7_000, 70_000));
        _history.AddSnapshot(new DeviceSnapshot(device.Id, Now.AddMinutes(-3), DeviceStatus.Online, responseTimeMs: 12));

        var history = await _service.GetHistoryAsync(device.Id, Now.AddHours(-1), Now);

        Assert.Equal(
            [
                new NetworkPoint(Now.AddMinutes(-3), null, null, 12),
                new NetworkPoint(Now.AddMinutes(-2), null, null, null),
                // 6000 bytes sent and 60 000 received in a minute.
                new NetworkPoint(Now.AddMinutes(-1), 100, 1000, null),
            ],
            history!.NetworkPoints);
    }

    [Fact]
    public void Traffic_ComparesEachInterfaceWithItself_AndSkipsCountersThatStartedAgain()
    {
        var start = Now.AddMinutes(-3);
        IReadOnlyList<NetworkSample> samples =
        [
            new(start, null, [new("Ethernet", 1_000, 1_000), new("Wi-Fi", 500_000, 500_000)]),
            // Wi-Fi disappeared and a VPN adapter appeared with its own large counters: neither counts.
            new(start.AddSeconds(10), null, [new("Ethernet", 2_000, 3_000), new("VPN", 900_000, 900_000)]),
            // Windows was restarted: the counters started from zero again.
            new(start.AddSeconds(20), null, [new("Ethernet", 10, 10)]),
        ];

        var points = DeviceHistoryService.ToNetworkPoints(samples);

        Assert.Equal((100.0, 200.0), (points[1].BytesSentPerSecond!.Value, points[1].BytesReceivedPerSecond!.Value));
        Assert.Null(points[2].BytesSentPerSecond);
    }

    [Fact]
    public void Downsample_AveragesTheKnownValuesOfEachPartOfThePeriod_AndKeepsShortHistoriesAsTheyAre()
    {
        var from = Now.AddHours(-1);
        var points = Enumerable.Range(0, 60)
            .Select(minute => new NetworkPoint(from.AddMinutes(minute), minute, 10 * minute, minute < 30 ? null : minute))
            .ToList();

        Assert.Same(points, DeviceHistoryService.Downsample(points, from, Now, maxPoints: 60));

        var reduced = DeviceHistoryService.Downsample(points, from, Now, maxPoints: 6);

        Assert.Equal(6, reduced.Count);

        // The first part holds minutes 0 to 9, in which the response time was not known.
        Assert.Equal(new NetworkPoint(from.AddMinutes(4.5), 4.5, 45, null), reduced[0]);
        Assert.Equal(54.5, reduced[5].ResponseTimeMs);
    }

    [Fact]
    public async Task UnknownDevice_HasNoHistory()
    {
        Assert.Null(await _service.GetHistoryAsync(Guid.NewGuid(), Now.AddHours(-1), Now));
    }

    [Fact]
    public async Task Period_MustEndAfterItStarts_AndMustNotBeTooLong()
    {
        var device = AddDevice(createdAt: Now.AddDays(-30));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetHistoryAsync(device.Id, Now, Now));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetHistoryAsync(device.Id, Now, Now.AddHours(-1)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetHistoryAsync(device.Id, Now.AddDays(-400), Now));
    }

    private Device AddDevice(DateTimeOffset createdAt)
    {
        var device = new Device(Guid.NewGuid(), "PC-01", MonitoringMode.Agent, "pc-01", null, createdAt);
        _devices.Add(device);

        return device;
    }

    private void AddOutage(Device device, DateTimeOffset startedAt, DateTimeOffset? endedAt)
    {
        var outage = new Outage(device.Id, startedAt);

        if (endedAt is not null)
        {
            outage.End(endedAt.Value);
        }

        _history.AddOutage(outage);
    }

    private void AddEvent(DateTimeOffset occurredAt, string message, Guid? deviceId)
    {
        _history.AddEvent(new MonitoringEvent(occurredAt, MonitoringEventType.PortOpened, EventSeverity.Info, message, deviceId));
    }

    private void AddSnapshot(Device device, DateTimeOffset recordedAt, (string Name, long Sent, long Received) networkInterface)
    {
        var snapshot = new DeviceSnapshot(device.Id, recordedAt, DeviceStatus.Online);
        snapshot.SetResources(new ResourceUsage(
            10,
            1000,
            500,
            [],
            [new NetworkInterfaceUsage(networkInterface.Name, null, null, isUp: true, networkInterface.Sent, networkInterface.Received)]));
        _history.AddSnapshot(snapshot);
    }
}
