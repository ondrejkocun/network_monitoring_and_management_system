using NetworkMonitoringSystem.Agent.Commands;

namespace NetworkMonitoringSystem.Agent;

public class Worker : BackgroundService
{
    private readonly AgentReporter _reporter;
    private readonly ILogger<Worker> _logger;
    private readonly SynchronizationSignal _synchronizationSignal;

    public Worker(AgentReporter reporter, SynchronizationSignal synchronizationSignal, ILogger<Worker> logger)
    {
        _synchronizationSignal = synchronizationSignal;
        _reporter = reporter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await _reporter.ReportOnceAsync(stoppingToken);

            _logger.LogDebug("Next report in {Delay}.", delay);

            // A command from the server can ask for the next report at once.
            await _synchronizationSignal.WaitAsync(delay, stoppingToken);
        }
    }
}
