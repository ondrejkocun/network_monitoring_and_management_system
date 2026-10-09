using NetworkMonitoringSystem.Application.Monitoring;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// Periodically marks devices that have not been seen for too long as offline.
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
            // While the server was not running no device could be seen, so devices first get time
            // to be seen again before anything is declared offline.
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

                return await monitor.GetStartupGracePeriodAsync(stoppingToken);
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
                _logger.LogWarning("{Count} device(s) were not seen for too long and were marked offline.", wentOffline);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One failed check (for example a database hiccup) must not stop monitoring.
            _logger.LogError(exception, "Availability check failed.");
        }
    }
}
