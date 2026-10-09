using NetworkMonitoringSystem.Application.Monitoring;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// Periodically deletes history older than the configured retention period, so the database does not grow without limit.
/// </summary>
public sealed class HistoryCleanupService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HistoryCleanupService> _logger;

    public HistoryCleanupService(IServiceScopeFactory scopeFactory, ILogger<HistoryCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The clean-up is not urgent; it starts once the server has finished starting up.
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await CleanUpAsync(stoppingToken);
                await Task.Delay(CleanupInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server is shutting down.
        }
    }

    private async Task CleanUpAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var retention = scope.ServiceProvider.GetRequiredService<IHistoryRetentionService>();

            var removed = await retention.CleanUpAsync(stoppingToken);

            if (removed.Total > 0)
            {
                _logger.LogInformation(
                    "Removed old history: {Snapshots} snapshot(s), {Outages} outage(s), {Events} event(s).",
                    removed.Snapshots,
                    removed.Outages,
                    removed.Events);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A failed clean-up is tried again in the next round.
            _logger.LogError(exception, "Clean-up of old history failed.");
        }
    }
}
