namespace NetworkMonitoringSystem.Domain.Monitoring;

/// <summary>
/// Global monitoring configuration. The system keeps exactly one instance.
/// </summary>
public sealed class MonitoringSettings
{
    public const int SingletonId = 1;

    public const int MinSyncIntervalSeconds = 5;
    public const int MaxSyncIntervalSeconds = 24 * 60 * 60;

    public const int MinOfflineAfterMissedSyncs = 1;
    public const int MaxOfflineAfterMissedSyncs = 100;

    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;

    public MonitoringSettings(
        int syncIntervalSeconds = 60,
        int offlineAfterMissedSyncs = 3,
        int retentionDays = 30,
        int topProcessCount = 10)
    {
        ChangeSyncInterval(syncIntervalSeconds);
        ChangeOfflineThreshold(offlineAfterMissedSyncs);
        ChangeRetention(retentionDays);
        ChangeTopProcessCount(topProcessCount);
    }

    public int Id { get; private set; } = SingletonId;

    /// <summary>How often device state is synchronized and recorded.</summary>
    public int SyncIntervalSeconds { get; private set; }

    /// <summary>Number of consecutive missed synchronizations after which a device is considered offline.</summary>
    public int OfflineAfterMissedSyncs { get; private set; }

    /// <summary>How long historical records are kept.</summary>
    public int RetentionDays { get; private set; }

    /// <summary>Number of most demanding processes whose usage is recorded in each snapshot.</summary>
    public int TopProcessCount { get; private set; }

    public void ChangeSyncInterval(int seconds)
    {
        if (seconds is < MinSyncIntervalSeconds or > MaxSyncIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                $"Synchronization interval must be between {MinSyncIntervalSeconds} and {MaxSyncIntervalSeconds} seconds.");
        }

        SyncIntervalSeconds = seconds;
    }

    public void ChangeOfflineThreshold(int missedSyncs)
    {
        if (missedSyncs is < MinOfflineAfterMissedSyncs or > MaxOfflineAfterMissedSyncs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(missedSyncs),
                missedSyncs,
                $"A device must be allowed to miss between {MinOfflineAfterMissedSyncs} and {MaxOfflineAfterMissedSyncs} synchronizations.");
        }

        OfflineAfterMissedSyncs = missedSyncs;
    }

    public void ChangeRetention(int days)
    {
        if (days is < MinRetentionDays or > MaxRetentionDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(days),
                days,
                $"History must be kept between {MinRetentionDays} and {MaxRetentionDays} days.");
        }

        RetentionDays = days;
    }

    public void ChangeTopProcessCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        TopProcessCount = count;
    }
}
