namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class HistoryRetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeviceId = Guid.NewGuid();

    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly MonitoringSettings _settings = new(retentionDays: 30);

    [Fact]
    public async Task CleanUpAsync_RemovesRecordsOlderThanRetention_AndKeepsNewerOnes()
    {
        var old = Now.AddDays(-30).AddSeconds(-1);
        var recent = Now.AddDays(-30).AddSeconds(1);
        _history.AddSnapshot(new DeviceSnapshot(DeviceId, old, DeviceStatus.Online));
        _history.AddSnapshot(new DeviceSnapshot(DeviceId, recent, DeviceStatus.Online));
        _history.AddEvent(new MonitoringEvent(old, MonitoringEventType.DeviceWentOnline, EventSeverity.Info, "old"));
        _history.AddEvent(new MonitoringEvent(recent, MonitoringEventType.DeviceWentOnline, EventSeverity.Info, "recent"));
        _history.AddOutage(EndedOutage(startedAt: old.AddHours(-1), endedAt: old));
        _history.AddOutage(EndedOutage(startedAt: old.AddHours(-1), endedAt: recent));

        var removed = await CreateService().CleanUpAsync();

        Assert.Equal(new HistoryCleanupResult(Snapshots: 1, Outages: 1, Events: 1), removed);
        Assert.Equal(3, removed.Total);
        Assert.Equal(recent, Assert.Single(_history.Snapshots).RecordedAt);
        Assert.Equal("recent", Assert.Single(_history.Events).Message);
        Assert.Equal(recent, Assert.Single(_history.Outages).EndedAt);
    }

    [Fact]
    public async Task CleanUpAsync_KeepsOutageThatIsStillInProgress_HoweverOld()
    {
        _history.AddOutage(new Outage(DeviceId, Now.AddDays(-400)));

        var removed = await CreateService().CleanUpAsync();

        Assert.Equal(0, removed.Total);
        Assert.True(Assert.Single(_history.Outages).IsOngoing);
    }

    [Fact]
    public async Task CleanUpAsync_UsesCurrentRetentionSetting()
    {
        _history.AddSnapshot(new DeviceSnapshot(DeviceId, Now.AddDays(-10), DeviceStatus.Online));
        var service = CreateService();
        Assert.Equal(0, (await service.CleanUpAsync()).Total);

        _settings.ChangeRetention(7);

        Assert.Equal(1, (await service.CleanUpAsync()).Snapshots);
    }

    private static Outage EndedOutage(DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        var outage = new Outage(DeviceId, startedAt);
        outage.End(endedAt);

        return outage;
    }

    private HistoryRetentionService CreateService()
    {
        return new HistoryRetentionService(new FixedSettingsRepository(_settings), _history, new MutableTimeProvider(Now));
    }
}
