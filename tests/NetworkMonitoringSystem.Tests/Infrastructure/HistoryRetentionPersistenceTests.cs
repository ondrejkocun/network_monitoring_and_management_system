namespace NetworkMonitoringSystem.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

/// <summary>
/// Runs the history clean-up against a real database, to verify the delete statements and the cascade.
/// </summary>
[Trait("Category", "Integration")]
public class HistoryRetentionPersistenceTests : IClassFixture<DatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public HistoryRetentionPersistenceTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task CleanUp_DeletesOldHistoryWithItsDetails_AndKeepsRecentHistoryAndOngoingOutages()
    {
        // The default retention is 30 days.
        var old = Now.AddDays(-31);
        var recent = Now.AddDays(-1);
        var device = new Device(Guid.NewGuid(), "PC-01", MonitoringMode.Agent, "pc-01", null, old);

        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.Devices.Add(device);
            dbContext.DeviceSnapshots.Add(MeasuredSnapshot(device.Id, old));
            dbContext.DeviceSnapshots.Add(MeasuredSnapshot(device.Id, recent));
            dbContext.Events.Add(new MonitoringEvent(old, MonitoringEventType.DeviceWentOffline, EventSeverity.Warning, "old", device.Id));
            dbContext.Events.Add(new MonitoringEvent(recent, MonitoringEventType.DeviceWentOnline, EventSeverity.Info, "recent", device.Id));

            var endedLongAgo = new Outage(device.Id, old.AddHours(-2));
            endedLongAgo.End(old);
            dbContext.Outages.Add(endedLongAgo);
            dbContext.Outages.Add(new Outage(device.Id, old.AddDays(-100)));

            await dbContext.SaveChangesAsync();
        }

        HistoryCleanupResult removed;

        await using (var dbContext = _database.CreateDbContext())
        {
            var service = new HistoryRetentionService(
                new EfMonitoringSettingsRepository(dbContext),
                new EfMonitoringHistoryRepository(dbContext),
                new MutableTimeProvider(Now));

            removed = await service.CleanUpAsync();
        }

        Assert.Equal(new HistoryCleanupResult(Snapshots: 1, Outages: 1, Events: 1), removed);

        await using var readContext = _database.CreateDbContext();

        var snapshot = await readContext.DeviceSnapshots
            .Include(snapshot => snapshot.Disks)
            .Include(snapshot => snapshot.NetworkInterfaces)
            .SingleAsync(snapshot => snapshot.DeviceId == device.Id);
        Assert.Equal(recent, snapshot.RecordedAt);
        Assert.Single(snapshot.Disks);
        Assert.Single(snapshot.NetworkInterfaces);

        // The disks and interfaces of the deleted snapshot are gone with it; only those of the kept one remain.
        Assert.Equal(1, await readContext.Set<DiskUsage>().CountAsync());
        Assert.Equal(1, await readContext.Set<NetworkInterfaceUsage>().CountAsync());

        Assert.Equal("recent", (await readContext.Events.SingleAsync(monitoringEvent => monitoringEvent.DeviceId == device.Id)).Message);

        var outage = await readContext.Outages.SingleAsync(outage => outage.DeviceId == device.Id);
        Assert.Null(outage.EndedAt);

        Assert.True(await readContext.Devices.AnyAsync(stored => stored.Id == device.Id));
    }

    private static DeviceSnapshot MeasuredSnapshot(Guid deviceId, DateTimeOffset recordedAt)
    {
        var snapshot = new DeviceSnapshot(deviceId, recordedAt, DeviceStatus.Online);
        snapshot.SetResources(new ResourceUsage(
            10,
            1000,
            500,
            [new DiskUsage("C:\\", 100, 40)],
            [new NetworkInterfaceUsage("Ethernet", null, "192.168.50.20", true, 1, 2)]));

        return snapshot;
    }
}
