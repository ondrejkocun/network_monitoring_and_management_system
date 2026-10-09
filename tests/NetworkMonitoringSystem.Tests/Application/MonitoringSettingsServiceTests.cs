namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class MonitoringSettingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly MonitoringSettings _settings = new(syncIntervalSeconds: 60);
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task GetAsync_ReturnsCurrentValuesAndLimits()
    {
        var settings = await CreateService().GetAsync();

        Assert.Equal(60, settings.SyncIntervalSeconds);
        Assert.Equal(3, settings.OfflineAfterMissedSyncs);
        Assert.Equal(MonitoringSettings.MinSyncIntervalSeconds, settings.MinSyncIntervalSeconds);
        Assert.Equal(MonitoringSettings.MaxSyncIntervalSeconds, settings.MaxSyncIntervalSeconds);
    }

    [Fact]
    public async Task ChangeSyncIntervalAsync_ChangesInterval_AndWritesEvent()
    {
        var result = await CreateService().ChangeSyncIntervalAsync(30);

        Assert.Equal(30, result.SyncIntervalSeconds);
        Assert.Equal(30, _settings.SyncIntervalSeconds);
        Assert.Equal(1, _unitOfWork.SaveCount);

        var settingsEvent = Assert.Single(_history.Events);
        Assert.Equal(MonitoringEventType.SettingsChanged, settingsEvent.Type);
        Assert.Equal(Now, settingsEvent.OccurredAt);
        Assert.Null(settingsEvent.DeviceId);
        Assert.Contains("60 s to 30 s", settingsEvent.Message);
    }

    [Fact]
    public async Task ChangeSyncIntervalAsync_WithSameValue_WritesNothing()
    {
        await CreateService().ChangeSyncIntervalAsync(60);

        Assert.Empty(_history.Events);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(86401)]
    public async Task ChangeSyncIntervalAsync_Throws_WhenIntervalIsOutsideLimits(int seconds)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateService().ChangeSyncIntervalAsync(seconds));

        Assert.Equal(60, _settings.SyncIntervalSeconds);
        Assert.Empty(_history.Events);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private MonitoringSettingsService CreateService()
    {
        return new MonitoringSettingsService(
            new FixedSettingsRepository(_settings),
            _history,
            _unitOfWork,
            new MutableTimeProvider(Now));
    }
}
