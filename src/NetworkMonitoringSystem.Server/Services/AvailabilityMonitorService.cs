using NetworkMonitoringSystem.Application.Monitoring;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// Periodically checks which agents stopped reporting and marks their devices as offline.
/// </summary>
public sealed class AvailabilityMonitorService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AvailabilityMonitorService> _logger;

    public AvailabilityMonitorService(IServiceScopeFactory scopeFactory, ILogger<AvailabilityMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // While the server was not running no agent could report, so agents first get the full
            // period to report again before anything is declared offline.
            await Task.Delay(await GetStartupGracePeriodAsync(stoppingToken), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await EvaluateAsync(stoppingToken);
                await Task.Delay(CheckInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server is shutting down.
        }
    }

    private async Task<TimeSpan> GetStartupGracePeriodAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var monitor = scope.ServiceProvider.GetRequiredService<IDeviceAvailabilityMonitor>();

                return await monitor.GetOfflineThresholdAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Monitoring settings could not be read; trying again.");
                await Task.Delay(RetryInterval, stoppingToken);
            }
        }
    }

    private async Task EvaluateAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var monitor = scope.ServiceProvider.GetRequiredService<IDeviceAvailabilityMonitor>();

            var wentOffline = await monitor.EvaluateAsync(stoppingToken);

            if (wentOffline > 0)
            {
                _logger.LogWarning("{Count} device(s) stopped reporting and were marked offline.", wentOffline);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One failed check (for example a database hiccup) must not stop monitoring.
            _logger.LogError(exception, "Availability check failed.");
        }
    }
}
