namespace NetworkMonitoringSystem.Tests.Domain;

using NetworkMonitoringSystem.Domain.Monitoring;

public class MonitoringSettingsTests
{
    [Fact]
    public void Constructor_UsesDefaults()
    {
        var settings = new MonitoringSettings();

        Assert.Equal(MonitoringSettings.SingletonId, settings.Id);
        Assert.Equal(60, settings.SyncIntervalSeconds);
        Assert.Equal(3, settings.OfflineAfterMissedSyncs);
        Assert.Equal(30, settings.RetentionDays);
        Assert.Equal(10, settings.TopProcessCount);
    }

    [Theory]
    [InlineData(MonitoringSettings.MinSyncIntervalSeconds)]
    [InlineData(300)]
    [InlineData(MonitoringSettings.MaxSyncIntervalSeconds)]
    public void ChangeSyncInterval_AcceptsValuesWithinLimits(int seconds)
    {
        var settings = new MonitoringSettings();

        settings.ChangeSyncInterval(seconds);

        Assert.Equal(seconds, settings.SyncIntervalSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MonitoringSettings.MinSyncIntervalSeconds - 1)]
    [InlineData(MonitoringSettings.MaxSyncIntervalSeconds + 1)]
    public void ChangeSyncInterval_RejectsValuesOutsideLimits(int seconds)
    {
        var settings = new MonitoringSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ChangeSyncInterval(seconds));
        Assert.Equal(60, settings.SyncIntervalSeconds);
    }

    [Fact]
    public void OtherSettings_RejectValuesBelowOne()
    {
        var settings = new MonitoringSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ChangeOfflineThreshold(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ChangeRetention(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ChangeTopProcessCount(0));
    }
}
