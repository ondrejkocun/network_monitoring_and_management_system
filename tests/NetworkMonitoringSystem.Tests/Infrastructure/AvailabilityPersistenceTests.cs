namespace NetworkMonitoringSystem.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Devices;
using NetworkMonitoringSystem.Infrastructure.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Persistence;
using NetworkMonitoringSystem.Tests.Fakes;

/// <summary>
/// Runs the availability logic against a real database, to verify the queries and that the history is stored.
/// </summary>
[Trait("Category", "Integration")]
public class AvailabilityPersistenceTests : IClassFixture<DatabaseFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public AvailabilityPersistenceTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task SilentDevice_GoesOffline_AndComesBack_WithStoredHistory()
    {
        var time = new MutableTimeProvider(Start);
        var deviceId = await AddOnlineAgentDeviceAsync(time);

        // The device stays silent for longer than 3 x 60 s.
        time.Advance(TimeSpan.FromMinutes(5));

        await using (var dbContext = _database.CreateDbContext())
        {
            await CreateMonitor(dbContext, time).EvaluateAsync();
        }

        await using (var dbContext = _database.CreateDbContext())
        {
            var device = await dbContext.Devices.SingleAsync(device => device.Id == deviceId);
            var outage = await dbContext.Outages.SingleAsync(outage => outage.DeviceId == deviceId);

            Assert.Equal(DeviceStatus.Offline, device.Status);
            Assert.Equal(Start, outage.StartedAt);
            Assert.Null(outage.EndedAt);
        }

        // The device reports again.
        time.Advance(TimeSpan.FromMinutes(5));

        await using (var dbContext = _database.CreateDbContext())
        {
            var device = await new EfDeviceRepository(dbContext).GetByIdAsync(deviceId);
            await new AvailabilityRecorder(new EfMonitoringHistoryRepository(dbContext)).RecordOnlineAsync(device!, time.GetUtcNow());
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _database.CreateDbContext())
        {
            var device = await dbContext.Devices.SingleAsync(device => device.Id == deviceId);
            var outage = await dbContext.Outages.SingleAsync(outage => outage.DeviceId == deviceId);
            var eventTypes = await dbContext.Events
                .Where(monitoringEvent => monitoringEvent.DeviceId == deviceId)
                .OrderBy(monitoringEvent => monitoringEvent.Id)
                .Select(monitoringEvent => monitoringEvent.Type)
                .ToListAsync();
            var snapshotCount = await dbContext.DeviceSnapshots.CountAsync(snapshot => snapshot.DeviceId == deviceId);

            Assert.Equal(DeviceStatus.Online, device.Status);
            Assert.Equal(Start.AddMinutes(10), outage.EndedAt);
            Assert.Equal([MonitoringEventType.DeviceWentOffline, MonitoringEventType.DeviceWentOnline], eventTypes);
            Assert.Equal(1, snapshotCount);
        }
    }

    [Fact]
    public async Task RecentlySeenDevice_StaysOnline()
    {
        var time = new MutableTimeProvider(Start);
        var deviceId = await AddOnlineAgentDeviceAsync(time);
        time.Advance(TimeSpan.FromMinutes(2));

        await using (var dbContext = _database.CreateDbContext())
        {
            await CreateMonitor(dbContext, time).EvaluateAsync();
        }

        await using var readContext = _database.CreateDbContext();
        var device = await readContext.Devices.SingleAsync(device => device.Id == deviceId);

        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.False(await readContext.Outages.AnyAsync(outage => outage.DeviceId == deviceId));
    }

    private async Task<Guid> AddOnlineAgentDeviceAsync(TimeProvider time)
    {
        var hostName = "pc-" + Guid.NewGuid().ToString("N");
        var device = new Device(Guid.NewGuid(), hostName, MonitoringMode.Agent, hostName, null, time.GetUtcNow());
        device.RecordContact(time.GetUtcNow());

        await using var dbContext = _database.CreateDbContext();
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync();

        return device.Id;
    }

    private static DeviceAvailabilityMonitor CreateMonitor(MonitoringDbContext dbContext, TimeProvider time)
    {
        return new DeviceAvailabilityMonitor(
            new EfDeviceRepository(dbContext),
            new EfMonitoringSettingsRepository(dbContext),
            new AvailabilityRecorder(new EfMonitoringHistoryRepository(dbContext)),
            new EfUnitOfWork(dbContext),
            time);
    }
}
