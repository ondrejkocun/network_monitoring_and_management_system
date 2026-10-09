using NetworkMonitoringSystem.Application.Monitoring;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// Checks devices without an agent from the server, once per global synchronization interval.
/// </summary>
public sealed class AgentlessCheckService : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentlessCheckService> _logger;

    public AgentlessCheckService(IServiceScopeFactory scopeFactory, ILogger<AgentlessCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(await CheckAsync(stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server is shutting down.
        }
    }

    private async Task<TimeSpan> CheckAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var checker = scope.ServiceProvider.GetRequiredService<IAgentlessDeviceChecker>();

            // The interval is read again every round, so a change takes effect without a restart.
            return await checker.CheckAllAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One failed round (for example a database hiccup) must not stop the checks.
            _logger.LogError(exception, "Check of devices without an agent failed.");

            return RetryInterval;
        }
    }
}
