namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class EventLogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMonitoringHistoryRepository _history = new();

    [Fact]
    public async Task GetAsync_ReturnsEventsOfDevicesAndOfTheSystemInThePeriod_NewestFirst()
    {
        _history.AddEvent(new MonitoringEvent(Now.AddHours(-2), MonitoringEventType.SettingsChanged, EventSeverity.Info, "too old"));
        _history.AddEvent(new MonitoringEvent(Now.AddMinutes(-30), MonitoringEventType.DeviceWentOffline, EventSeverity.Warning, "offline", Guid.NewGuid()));
        _history.AddEvent(new MonitoringEvent(Now.AddMinutes(-10), MonitoringEventType.AccessDenied, EventSeverity.Warning, "refused", userId: Guid.NewGuid()));

        var log = await new EventLogService(_history).GetAsync(Now.AddHours(-1), Now);

        Assert.Equal(["refused", "offline"], log.Events.Select(entry => entry.Message));
        Assert.False(log.Truncated);
    }

    [Fact]
    public async Task GetAsync_ReturnsAtMostTheLimit_AndSaysSomethingWasLeftOut()
    {
        for (var i = 0; i < EventLogService.MaxEvents + 5; i++)
        {
            _history.AddEvent(new MonitoringEvent(Now.AddSeconds(-i), MonitoringEventType.UserLoggedIn, EventSeverity.Info, $"event {i}"));
        }

        var log = await new EventLogService(_history).GetAsync(Now.AddHours(-1), Now);

        Assert.Equal(EventLogService.MaxEvents, log.Events.Count);
        Assert.True(log.Truncated);
        Assert.Equal("event 0", log.Events[0].Message);
    }

    [Fact]
    public async Task GetAsync_RejectsPeriodThatEndsBeforeItStarts()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new EventLogService(_history).GetAsync(Now, Now.AddSeconds(-1)));
    }
}
