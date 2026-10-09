namespace NetworkMonitoringSystem.Agent;

public class Worker : BackgroundService
{
    private readonly AgentReporter _reporter;
    private readonly ILogger<Worker> _logger;

    public Worker(AgentReporter reporter, ILogger<Worker> logger)
    {
        _reporter = reporter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await _reporter.ReportOnceAsync(stoppingToken);

            _logger.LogDebug("Next report in {Delay}.", delay);

            await Task.Delay(delay, stoppingToken);
        }
    }
}
