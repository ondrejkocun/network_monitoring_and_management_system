namespace NetworkMonitoringSystem.Tests.Agent;

using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Commands;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Agents;

/// <summary>An unexpected failure in one round must not end the agent's loops, which would stop the whole service.</summary>
public class AgentLoopFailureTests
{
    [Fact]
    public async Task Report_ThatFailsUnexpectedly_IsRetriedLater()
    {
        using var channel = GrpcChannel.ForAddress("https://localhost:1");
        var reporter = new AgentReporter(
            new AgentApi.AgentApiClient(channel),
            new BrokenIdentityStore(),
            null!,
            null!,
            null!,
            Options.Create(new AgentOptions { RetryIntervalSeconds = 7 }),
            NullLogger<AgentReporter>.Instance);

        Assert.Equal(TimeSpan.FromSeconds(7), await reporter.ReportOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CommandPoll_ThatFailsUnexpectedly_IsRetriedLater()
    {
        using var channel = GrpcChannel.ForAddress("https://localhost:1");
        var worker = new CommandWorker(
            new AgentApi.AgentApiClient(channel),
            new BrokenIdentityStore(),
            null!,
            null!,
            NullLogger<CommandWorker>.Instance);

        Assert.True(await worker.PollOnceAsync(CancellationToken.None) > TimeSpan.Zero);
    }

    private sealed class BrokenIdentityStore : IAgentIdentityStore
    {
        public AgentIdentity? Load() => throw new IOException("The disk is not ready.");

        public void Save(AgentIdentity identity) => throw new IOException("The disk is not ready.");

        public void Clear() => throw new IOException("The disk is not ready.");
    }
}
