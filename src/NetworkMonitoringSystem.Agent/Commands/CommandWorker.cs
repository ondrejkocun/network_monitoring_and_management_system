using Grpc.Core;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Commands;

/// <summary>
/// Asks the server for commands every few seconds, carries them out one after another and reports the results.
/// </summary>
public sealed class CommandWorker : BackgroundService
{
    private const int MaxResultLength = 4000;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(90);

    /// <summary>A file of up to 100 MB may take longer than a command; the server keeps it for 10 minutes.</summary>
    private static readonly TimeSpan FileTimeout = TimeSpan.FromMinutes(9);

    private readonly AgentApi.AgentApiClient _client;
    private readonly IAgentIdentityStore _identityStore;
    private readonly ICommandExecutor _executor;
    private readonly IFileReceiver _fileReceiver;
    private readonly ILogger<CommandWorker> _logger;

    public CommandWorker(
        AgentApi.AgentApiClient client,
        IAgentIdentityStore identityStore,
        ICommandExecutor executor,
        IFileReceiver fileReceiver,
        ILogger<CommandWorker> logger)
    {
        _client = client;
        _identityStore = identityStore;
        _executor = executor;
        _fileReceiver = fileReceiver;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await PollOnceAsync(stoppingToken);

            await Task.Delay(delay, stoppingToken);
        }
    }

    /// <summary>Fetches and carries out the waiting commands. Returns how long to wait before asking again.</summary>
    public async Task<TimeSpan> PollOnceAsync(CancellationToken cancellationToken)
    {
        // An agent that has not registered yet has no commands; the reporting loop registers it.
        if (_identityStore.Load() is not { } identity)
        {
            return DefaultPollInterval;
        }

        var credentials = new AgentCredentials { DeviceId = identity.DeviceId, AgentKey = identity.AgentKey };

        try
        {
            var reply = await _client.FetchCommandsAsync(new FetchCommandsRequest { Credentials = credentials }, cancellationToken: cancellationToken);

            foreach (var command in reply.Commands)
            {
                var result = await ExecuteAsync(command, credentials, cancellationToken);

                await _client.ReportCommandResultAsync(
                    new CommandResultRequest
                    {
                        Credentials = credentials,
                        CommandId = command.Id,
                        Succeeded = result.Succeeded,
                        Message = result.Message.Length <= MaxResultLength ? result.Message : result.Message[..MaxResultLength],
                    },
                    cancellationToken: cancellationToken);
            }

            return reply.PollIntervalSeconds > 0 ? TimeSpan.FromSeconds(reply.PollIntervalSeconds) : DefaultPollInterval;
        }
        catch (RpcException exception) when (exception.StatusCode != StatusCode.Cancelled)
        {
            // The reporting loop already logs an unreachable server and repairs a rejected identity.
            _logger.LogDebug("Commands could not be fetched or reported ({Status}).", exception.StatusCode);

            return DefaultPollInterval;
        }
    }

    private async Task<CommandResult> ExecuteAsync(AgentCommand command, AgentCredentials credentials, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Carrying out command {Type} {CommandId}.", command.Type, command.Id);

        var isFile = command.Type == CommandType.SendFile;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(isFile ? FileTimeout : CommandTimeout);

        CommandResult result;

        try
        {
            // A file is downloaded from the server; every other command is carried out on this computer.
            result = isFile
                ? await _fileReceiver.ReceiveAsync(command, credentials, timeout.Token)
                : await _executor.ExecuteAsync(command, timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Whatever goes wrong, the server must learn that the command did not succeed.
            _logger.LogError(exception, "Command {Type} {CommandId} failed unexpectedly.", command.Type, command.Id);
            result = CommandResult.Failure("The agent failed to carry the command out.");
        }

        _logger.LogInformation(
            "Command {Type} {CommandId} {Outcome}.",
            command.Type,
            command.Id,
            result.Succeeded ? "succeeded" : "failed");

        return result;
    }
}
