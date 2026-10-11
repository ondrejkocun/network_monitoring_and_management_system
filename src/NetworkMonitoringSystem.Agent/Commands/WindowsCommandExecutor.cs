using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Commands;

/// <summary>How a command ended and what there is to tell about it.</summary>
public sealed record CommandResult(bool Succeeded, string Message)
{
    public static CommandResult Success(string message) => new(true, message);

    public static CommandResult Failure(string message) => new(false, message);
}

public interface ICommandExecutor
{
    /// <summary>Carries the command out. Never throws for a command that cannot be carried out; it reports a failure.</summary>
    Task<CommandResult> ExecuteAsync(AgentCommand command, CancellationToken cancellationToken);
}

/// <summary>Starts one of the few system programs the agent uses, always with arguments the agent itself wrote.</summary>
public interface ISystemProgramRunner
{
    /// <param name="programName">Name of a program in the Windows system directory.</param>
    Task<(int ExitCode, string Output)> RunAsync(string programName, string arguments, CancellationToken cancellationToken);
}

/// <summary>
/// Carries out the fixed list of commands on Windows. Nothing received from the server is ever passed
/// to a command line: parameters are parsed into a number, a service name or an address and used through
/// the system's programming interfaces. The parameters are checked here again, although the server checks them too.
/// </summary>
public sealed class WindowsCommandExecutor : ICommandExecutor
{
    /// <summary>The delay gives the agent time to report the result before the system goes down.</summary>
    public const int ShutdownDelaySeconds = 30;

    private const int PingCount = 4;
    private const int PingTimeoutMilliseconds = 2000;
    private const int MaxHops = 30;

    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    // Ending one of these takes the whole system down, which is what the restart and shut-down commands are for.
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "Registry",
    };

    private readonly ISystemProgramRunner _programRunner;
    private readonly SynchronizationSignal _synchronizationSignal;

    public WindowsCommandExecutor(ISystemProgramRunner programRunner, SynchronizationSignal synchronizationSignal)
    {
        _programRunner = programRunner;
        _synchronizationSignal = synchronizationSignal;
    }

    public async Task<CommandResult> ExecuteAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return command.Type switch
            {
                CommandType.RestartDevice => await ShutDownAsync(restart: true, cancellationToken),
                CommandType.ShutDownDevice => await ShutDownAsync(restart: false, cancellationToken),
                CommandType.KillProcess => KillProcess(command.Parameter),
                CommandType.StartService => ControlService(command.Parameter, start: true, stop: false),
                CommandType.StopService => ControlService(command.Parameter, start: false, stop: true),
                CommandType.RestartService => ControlService(command.Parameter, start: true, stop: true),
                CommandType.SynchronizeNow => SynchronizeNow(),
                CommandType.Ping => await PingAsync(command.Parameter, cancellationToken),
                CommandType.Traceroute => await TracerouteAsync(command.Parameter, cancellationToken),
                CommandType.FlushDnsCache => await FlushDnsCacheAsync(cancellationToken),
                _ => CommandResult.Failure("The agent does not know this command."),
            };
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Failure("The command did not finish in time.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException
            or PingException or System.Net.Sockets.SocketException or System.ServiceProcess.TimeoutException)
        {
            return CommandResult.Failure(Describe(exception));
        }
    }

    private async Task<CommandResult> ShutDownAsync(bool restart, CancellationToken cancellationToken)
    {
        var arguments = string.Create(
            CultureInfo.InvariantCulture,
            $"{(restart ? "/r" : "/s")} /t {ShutdownDelaySeconds} /c \"Requested by the administrator of the monitoring system.\"");

        var (exitCode, output) = await _programRunner.RunAsync("shutdown.exe", arguments, cancellationToken);

        return exitCode == 0
            ? CommandResult.Success($"The device will {(restart ? "restart" : "shut down")} in {ShutdownDelaySeconds} seconds.")
            : CommandResult.Failure($"The system refused (code {exitCode}). {output}".Trim());
    }

    private static CommandResult KillProcess(string parameter)
    {
        if (!int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
        {
            return CommandResult.Failure("The process identifier is not valid.");
        }

        if (pid == Environment.ProcessId)
        {
            return CommandResult.Failure("The agent does not end itself.");
        }

        Process process;

        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return CommandResult.Failure($"No process with the identifier {pid} is running.");
        }

        using (process)
        {
            var name = process.ProcessName;

            if (pid <= 4 || ProtectedProcessNames.Contains(name))
            {
                return CommandResult.Failure($"The process '{name}' ({pid}) is part of the system and is not ended.");
            }

            process.Kill();

            return process.WaitForExit(5000)
                ? CommandResult.Success($"The process '{name}' ({pid}) was ended.")
                : CommandResult.Failure($"The process '{name}' ({pid}) was asked to end but is still running.");
        }
    }

    private static CommandResult ControlService(string parameter, bool start, bool stop)
    {
        var name = parameter.Trim();

        if (name.Length is 0 or > 80 || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-' or '$' or ' ')))
        {
            return CommandResult.Failure("The service name is not valid.");
        }

        using var service = new ServiceController(name);

        ServiceControllerStatus status;

        try
        {
            status = service.Status;
        }
        catch (InvalidOperationException)
        {
            return CommandResult.Failure($"The service '{name}' does not exist on this device.");
        }

        if (stop && status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
        }
        else if (stop && !start)
        {
            return CommandResult.Success($"The service '{name}' was already stopped.");
        }

        if (start)
        {
            service.Refresh();

            if (service.Status == ServiceControllerStatus.Running)
            {
                return CommandResult.Success($"The service '{name}' was already running.");
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
        }

        return CommandResult.Success($"The service '{name}' is {(start ? "running" : "stopped")}.");
    }

    private CommandResult SynchronizeNow()
    {
        _synchronizationSignal.Request();

        return CommandResult.Success("The agent reports its state now.");
    }

    private static async Task<CommandResult> PingAsync(string parameter, CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(parameter, cancellationToken);

        if (target is null)
        {
            return CommandResult.Failure($"The address '{Shown(parameter)}' could not be resolved.");
        }

        using var ping = new Ping();
        var output = new StringBuilder($"Ping {target}:").AppendLine();
        var received = 0;

        for (var attempt = 0; attempt < PingCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reply = await ping.SendPingAsync(target, PingTimeoutMilliseconds);

            if (reply.Status == IPStatus.Success)
            {
                received++;
                output.AppendLine(CultureInfo.InvariantCulture, $"  reply in {reply.RoundtripTime} ms");
            }
            else
            {
                output.AppendLine(CultureInfo.InvariantCulture, $"  no reply ({reply.Status})");
            }
        }

        output.Append(CultureInfo.InvariantCulture, $"{received} of {PingCount} replies received.");

        return new CommandResult(received > 0, output.ToString());
    }

    private static async Task<CommandResult> TracerouteAsync(string parameter, CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(parameter, cancellationToken);

        if (target is null)
        {
            return CommandResult.Failure($"The address '{Shown(parameter)}' could not be resolved.");
        }

        using var ping = new Ping();
        var payload = new byte[32];
        var output = new StringBuilder($"Route to {target}:").AppendLine();

        // The same as the system's tracert: each probe may travel one hop further than the previous one.
        for (var hop = 1; hop <= MaxHops; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reply = await ping.SendPingAsync(target, PingTimeoutMilliseconds, payload, new PingOptions(hop, dontFragment: true));

            if (reply.Status is IPStatus.TtlExpired or IPStatus.Success)
            {
                output.AppendLine(CultureInfo.InvariantCulture, $"  {hop,2}  {reply.Address}  {reply.RoundtripTime} ms");
            }
            else
            {
                output.AppendLine(CultureInfo.InvariantCulture, $"  {hop,2}  *  ({reply.Status})");
            }

            if (reply.Status == IPStatus.Success)
            {
                output.Append(CultureInfo.InvariantCulture, $"Reached in {hop} hop(s).");

                return CommandResult.Success(output.ToString());
            }
        }

        output.Append(CultureInfo.InvariantCulture, $"Not reached within {MaxHops} hops.");

        return CommandResult.Failure(output.ToString());
    }

    private async Task<CommandResult> FlushDnsCacheAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output) = await _programRunner.RunAsync("ipconfig.exe", "/flushdns", cancellationToken);

        return exitCode == 0
            ? CommandResult.Success("The DNS cache was flushed.")
            : CommandResult.Failure($"The system refused (code {exitCode}). {output}".Trim());
    }

    private static async Task<IPAddress?> ResolveAsync(string parameter, CancellationToken cancellationToken)
    {
        var text = parameter.Trim();

        if (IPAddress.TryParse(text, out var address))
        {
            return address;
        }

        // A name is resolved only when it looks like one; anything else is not even tried.
        if (text.Length is 0 or > 253 || Uri.CheckHostName(text) != UriHostNameType.Dns)
        {
            return null;
        }

        try
        {
            return (await Dns.GetHostAddressesAsync(text, cancellationToken)).FirstOrDefault();
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    /// <summary>A parameter that failed validation is quoted back only in a short and harmless form.</summary>
    private static string Shown(string parameter)
    {
        var printable = new string(parameter.Where(character => !char.IsControl(character)).Take(60).ToArray());

        return printable;
    }

    private static string Describe(Exception exception)
    {
        return exception.InnerException is Win32Exception inner ? $"{exception.Message} {inner.Message}" : exception.Message;
    }
}

/// <summary>Runs programs from the Windows system directory and nothing else.</summary>
public sealed class SystemProgramRunner : ISystemProgramRunner
{
    public async Task<(int ExitCode, string Output)> RunAsync(string programName, string arguments, CancellationToken cancellationToken)
    {
        // The full path keeps a program of the same name placed elsewhere from being started instead.
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, programName), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"The program '{programName}' could not be started.");

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, $"{await output} {await error}".Trim());
    }
}

/// <summary>Lets a command ask the reporting loop to report at once instead of waiting for the interval.</summary>
public sealed class SynchronizationSignal
{
    private readonly SemaphoreSlim _requested = new(0, 1);

    public void Request()
    {
        // One waiting request is enough; more of them would only repeat the report.
        if (_requested.CurrentCount == 0)
        {
            try
            {
                _requested.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another request got in first.
            }
        }
    }

    /// <summary>Waits for the delay or for a request, whichever comes first.</summary>
    /// <returns>True when the wait ended because of a request.</returns>
    public Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return _requested.WaitAsync(delay, cancellationToken);
    }
}
