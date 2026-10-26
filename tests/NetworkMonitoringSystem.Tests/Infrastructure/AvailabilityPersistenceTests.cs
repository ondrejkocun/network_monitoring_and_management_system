namespace NetworkMonitoringSystem.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Common;
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

    [Fact]
    public async Task UpdateFromAnOlderCopyOfTheDevice_IsRefused_AndOverwritesNothing()
    {
        var time = new MutableTimeProvider(Start);
        var deviceId = await AddOnlineAgentDeviceAsync(time);

        await using var staleContext = _database.CreateDbContext();
        var staleCopy = await staleContext.Devices.SingleAsync(device => device.Id == deviceId);

        time.Advance(TimeSpan.FromMinutes(1));
        await RecordReportAsync(deviceId, time.GetUtcNow());

        staleCopy.MarkOffline();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(staleContext).SaveChangesAsync());

        await using var readContext = _database.CreateDbContext();
        var stored = await readContext.Devices.SingleAsync(device => device.Id == deviceId);
        Assert.Equal(DeviceStatus.Online, stored.Status);
        Assert.Equal(Start.AddMinutes(1), stored.LastSeenAt);
    }

    [Fact]
    public async Task ReportArrivingWhileDeviceIsBeingMarkedOffline_LeavesItOnline_WithoutOutage()
    {
        var time = new MutableTimeProvider(Start);
        var deviceId = await AddOnlineAgentDeviceAsync(time);
        time.Advance(TimeSpan.FromMinutes(5));

        await using (var dbContext = _database.CreateDbContext())
        {
            // The check has already loaded the silent device and decided it is offline; just before it saves,
            // the agent's report is stored.
            var unitOfWork = new InterleavingUnitOfWork(
                new EfUnitOfWork(dbContext),
                () => RecordReportAsync(deviceId, time.GetUtcNow()));

            // Devices of other tests in the same database may go offline as well, so only this device is checked.
            await new DeviceAvailabilityMonitor(
                new EfDeviceRepository(dbContext),
                new EfMonitoringSettingsRepository(dbContext),
                new AvailabilityRecorder(new EfMonitoringHistoryRepository(dbContext)),
                unitOfWork,
                time).EvaluateAsync();
        }

        await using var readContext = _database.CreateDbContext();
        var device = await readContext.Devices.SingleAsync(device => device.Id == deviceId);

        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(Start.AddMinutes(5), device.LastSeenAt);
        Assert.False(await readContext.Outages.AnyAsync(outage => outage.DeviceId == deviceId));
        Assert.False(await readContext.Events.AnyAsync(monitoringEvent =>
            monitoringEvent.DeviceId == deviceId && monitoringEvent.Type == MonitoringEventType.DeviceWentOffline));
    }

    /// <summary>Stores a report of the device's agent through a separate connection, as a concurrent request would.</summary>
    private async Task RecordReportAsync(Guid deviceId, DateTimeOffset seenAt)
    {
        await using var dbContext = _database.CreateDbContext();
        var device = await new EfDeviceRepository(dbContext).GetByIdAsync(deviceId);

        await new AvailabilityRecorder(new EfMonitoringHistoryRepository(dbContext)).RecordOnlineAsync(device!, seenAt);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Runs another operation once, right before the first save, to make two updates overlap.</summary>
    private sealed class InterleavingUnitOfWork(IUnitOfWork inner, Func<Task> beforeFirstSave) : IUnitOfWork
    {
        private Func<Task>? _beforeFirstSave = beforeFirstSave;

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_beforeFirstSave is { } operation)
            {
                _beforeFirstSave = null;
                await operation();
            }

            await inner.SaveChangesAsync(cancellationToken);
        }

        public void DiscardChanges() => inner.DiscardChanges();
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
