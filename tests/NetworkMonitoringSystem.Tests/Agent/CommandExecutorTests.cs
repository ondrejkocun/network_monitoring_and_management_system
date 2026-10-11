namespace NetworkMonitoringSystem.Tests.Agent;

using System.Diagnostics;
using NetworkMonitoringSystem.Agent.Commands;
using NetworkMonitoringSystem.Contracts.Agents;

/// <summary>
/// Runs the commands on this computer where that is harmless. Restart, shut-down and flushing the DNS cache
/// go through a stand-in for the system programs, so the tests only check what would be started.
/// </summary>
public class CommandExecutorTests
{
    private readonly RecordingProgramRunner _programs = new();
    private readonly SynchronizationSignal _signal = new();
    private readonly WindowsCommandExecutor _executor;

    public CommandExecutorTests()
    {
        _executor = new WindowsCommandExecutor(_programs, _signal);
    }

    [Fact]
    public async Task Ping_OfThisComputer_Succeeds_AndReportsReplies()
    {
        var result = await ExecuteAsync(CommandType.Ping, "127.0.0.1");

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("Ping 127.0.0.1", result.Message);
        Assert.Contains("of 4 replies received", result.Message);
    }

    [Fact]
    public async Task Traceroute_ToThisComputer_ReachesItInOneHop()
    {
        var result = await ExecuteAsync(CommandType.Traceroute, "127.0.0.1");

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("Reached in 1 hop", result.Message);
    }

    [Theory]
    [InlineData(CommandType.Ping, "")]
    [InlineData(CommandType.Ping, "8.8.8.8 & calc")]
    [InlineData(CommandType.Ping, "-t")]
    [InlineData(CommandType.Traceroute, "host name")]
    [InlineData(CommandType.Ping, "this-host-does-not-exist.invalid")]
    public async Task Ping_AndTraceroute_ToSomethingThatIsNotAnAddress_Fail_WithoutStartingAnything(CommandType type, string target)
    {
        var result = await ExecuteAsync(type, target);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be resolved", result.Message);
        Assert.Empty(_programs.Started);
    }

    [Fact]
    public async Task KillProcess_EndsTheProcess()
    {
        // A harmless process of our own that would otherwise run for a minute.
        using var victim = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 60 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        try
        {
            var result = await ExecuteAsync(CommandType.KillProcess, victim.Id.ToString());

            Assert.True(result.Succeeded, result.Message);
            Assert.True(victim.WaitForExit(5000));
            Assert.Contains($"({victim.Id}) was ended", result.Message);
        }
        finally
        {
            if (!victim.HasExited)
            {
                victim.Kill();
            }
        }
    }

    [Fact]
    public async Task KillProcess_RefusesTheAgentItself_SystemProcesses_AndWhatIsNotAProcessIdentifier()
    {
        var itself = await ExecuteAsync(CommandType.KillProcess, Environment.ProcessId.ToString());
        Assert.False(itself.Succeeded);
        Assert.Contains("does not end itself", itself.Message);

        var system = await ExecuteAsync(CommandType.KillProcess, "4");
        Assert.False(system.Succeeded);
        Assert.Contains("part of the system", system.Message);

        using var critical = Process.GetProcessesByName("csrss").First();
        Assert.Contains("part of the system", (await ExecuteAsync(CommandType.KillProcess, critical.Id.ToString())).Message);

        foreach (var parameter in new[] { "", "0", "-1", "12 & calc", "notepad" })
        {
            var invalid = await ExecuteAsync(CommandType.KillProcess, parameter);
            Assert.False(invalid.Succeeded);
            Assert.Contains("not valid", invalid.Message);
        }
    }

    [Fact]
    public async Task KillProcess_OfProcessThatIsNotRunning_Fails()
    {
        // Identifiers are multiples of four on Windows, so this one never belongs to a process.
        var result = await ExecuteAsync(CommandType.KillProcess, "2147483645");

        Assert.False(result.Succeeded);
        Assert.Contains("No process", result.Message);
    }

    [Theory]
    [InlineData(CommandType.StartService)]
    [InlineData(CommandType.StopService)]
    [InlineData(CommandType.RestartService)]
    public async Task ServiceCommand_ForServiceThatDoesNotExist_OrInvalidName_Fails(CommandType type)
    {
        var missing = await ExecuteAsync(type, "NmsTestServiceThatDoesNotExist");
        Assert.False(missing.Succeeded);
        Assert.Contains("does not exist", missing.Message);

        foreach (var name in new[] { "", "Spooler & calc", "a\"b", "..\\x", new string('a', 81) })
        {
            var invalid = await ExecuteAsync(type, name);
            Assert.False(invalid.Succeeded);
            Assert.Contains("not valid", invalid.Message);
        }
    }

    [Fact]
    public async Task StartService_ThatIsAlreadyRunning_Succeeds_WithoutChangingIt()
    {
        // The event log service runs on every Windows computer; asking to start it changes nothing.
        var result = await ExecuteAsync(CommandType.StartService, "EventLog");

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("already running", result.Message);
    }

    [Theory]
    [InlineData(CommandType.RestartDevice, "/r /t 30", "restart")]
    [InlineData(CommandType.ShutDownDevice, "/s /t 30", "shut down")]
    public async Task Restart_AndShutDown_StartTheSystemProgram_WithFixedArguments_AndADelay(CommandType type, string expectedArguments, string expectedWord)
    {
        // Whatever arrives as a parameter is ignored; nothing from the server reaches the command line.
        var result = await ExecuteAsync(type, "/a & calc");

        var (program, arguments) = Assert.Single(_programs.Started);
        Assert.Equal("shutdown.exe", program);
        Assert.StartsWith(expectedArguments + " /c \"", arguments);
        Assert.DoesNotContain("calc", arguments);
        Assert.True(result.Succeeded);
        Assert.Contains($"will {expectedWord} in 30 seconds", result.Message);
    }

    [Fact]
    public async Task FlushDnsCache_StartsIpconfig_AndReportsWhenTheSystemRefuses()
    {
        Assert.True((await ExecuteAsync(CommandType.FlushDnsCache, string.Empty)).Succeeded);
        Assert.Equal(("ipconfig.exe", "/flushdns"), Assert.Single(_programs.Started));

        _programs.ExitCode = 1;
        _programs.Output = "The requested operation requires elevation.";

        var refused = await ExecuteAsync(CommandType.FlushDnsCache, string.Empty);
        Assert.False(refused.Succeeded);
        Assert.Contains("requires elevation", refused.Message);
    }

    [Fact]
    public async Task SynchronizeNow_WakesTheReportingLoop_Once()
    {
        Assert.False(await _signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));

        Assert.True((await ExecuteAsync(CommandType.SynchronizeNow, string.Empty)).Succeeded);
        Assert.True((await ExecuteAsync(CommandType.SynchronizeNow, string.Empty)).Succeeded);

        Assert.True(await _signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.False(await _signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownCommand_Fails_AndCancelledCommand_ReportsThatItDidNotFinish()
    {
        var unknown = await ExecuteAsync((CommandType)99, string.Empty);
        Assert.False(unknown.Succeeded);
        Assert.Empty(_programs.Started);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await _executor.ExecuteAsync(new AgentCommand { Id = "c", Type = CommandType.Ping, Parameter = "127.0.0.1" }, cancelled.Token);
        Assert.False(result.Succeeded);
        Assert.Contains("did not finish in time", result.Message);
    }

    private Task<CommandResult> ExecuteAsync(CommandType type, string parameter)
    {
        return _executor.ExecuteAsync(
            new AgentCommand { Id = Guid.NewGuid().ToString(), Type = type, Parameter = parameter },
            CancellationToken.None);
    }

    private sealed class RecordingProgramRunner : ISystemProgramRunner
    {
        public List<(string Program, string Arguments)> Started { get; } = [];

        public int ExitCode { get; set; }

        public string Output { get; set; } = string.Empty;

        public Task<(int ExitCode, string Output)> RunAsync(string programName, string arguments, CancellationToken cancellationToken)
        {
            Started.Add((programName, arguments));

            return Task.FromResult((ExitCode, Output));
        }
    }
}
