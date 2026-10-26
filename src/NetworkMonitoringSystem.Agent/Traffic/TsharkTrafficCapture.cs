using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Traffic;

/// <summary>Captures the device's network traffic while the server asks for it and sums it per conversation.</summary>
public interface ITrafficCapture : IDisposable
{
    /// <summary>True while the server asks for traffic, whether or not capture actually works.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Starts or stops capturing. While enabled, a capture that stopped on its own is started again. Never throws:
    /// a failure is reported as the capture problem in the next summary.
    /// </summary>
    void SetEnabled(bool enabled);

    /// <summary>Returns the traffic counted since the previous summary, or why it could not be captured.</summary>
    TrafficSummary TakeSummary();
}

/// <summary>
/// Runs TShark (the command line of Wireshark) and reads only packet headers from it. TShark runs without promiscuous
/// mode, so only traffic of this device is seen, and is reset regularly, so it does not keep growing in memory.
/// </summary>
public sealed partial class TsharkTrafficCapture : ITrafficCapture
{
    /// <summary>TShark forgets what it learned about earlier packets after this many, to keep its memory flat.</summary>
    private const int SessionResetPackets = 100_000;

    private const int MaxErrorLines = 5;

    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TsharkTrafficCapture> _logger;
    private readonly object _lock = new();
    private readonly Queue<string> _errorLines = new();

    private Process? _process;
    private TrafficAggregator? _aggregator;
    private string? _problem;
    private DateTimeOffset _problemSince;

    public TsharkTrafficCapture(IOptions<AgentOptions> options, ILogger<TsharkTrafficCapture> logger, TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _problemSince = _timeProvider.GetUtcNow();
    }

    public bool IsEnabled { get; private set; }

    /// <summary>The TShark program used, from the configuration or where Wireshark installs it.</summary>
    public string TsharkPath => string.IsNullOrWhiteSpace(_options.TsharkPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Wireshark", "tshark.exe")
        : _options.TsharkPath;

    public void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            IsEnabled = enabled;

            if (!enabled)
            {
                Stop();
                _problem = null;

                return;
            }

            if (_process is { HasExited: false })
            {
                return;
            }

            try
            {
                Start();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ReportProblem($"Traffic capture could not be started: {exception.Message}");
            }
        }
    }

    public TrafficSummary TakeSummary()
    {
        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();
            var summary = _aggregator?.TakeSummary(now) ?? new TrafficSummary
            {
                PeriodStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(_problemSince),
                PeriodEnd = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now),
            };

            if (_process is { HasExited: true } && _problem is null)
            {
                lock (_errorLines)
                {
                    ReportProblem("TShark stopped unexpectedly: " + string.Join(" ", _errorLines));
                }
            }

            summary.CaptureProblem = _problem ?? string.Empty;
            _problemSince = now;

            return summary;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            Stop();
        }
    }

    /// <summary>
    /// Returns the Npcap interfaces TShark can capture on that belong to active network adapters of this computer.
    /// </summary>
    public static IReadOnlyList<string> SelectInterfaces(string tsharkInterfaceList, IEnumerable<NetworkInterface> adapters)
    {
        var activeIds = adapters
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && adapter.GetIPProperties().UnicastAddresses.Count > 0)
            .Select(adapter => adapter.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return NpcapDevice().Matches(tsharkInterfaceList)
            .Where(match => activeIds.Contains(match.Groups["id"].Value))
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Start()
    {
        Stop();

        lock (_errorLines)
        {
            _errorLines.Clear();
        }

        var tshark = TsharkPath;

        if (!File.Exists(tshark))
        {
            ReportProblem($"TShark was not found at '{tshark}'. Install Wireshark with Npcap on this computer.");

            return;
        }

        var interfaces = _options.CaptureInterfaces is { Length: > 0 } configured
            ? configured
            : SelectInterfaces(ListInterfaces(tshark), NetworkInterface.GetAllNetworkInterfaces()).ToArray();

        if (interfaces.Length == 0)
        {
            ReportProblem("No active network interface can be captured on. Check that Npcap is installed.");

            return;
        }

        var startInfo = new ProcessStartInfo(tshark)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var name in interfaces)
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(name);
        }

        // -p: only this device's traffic; -n: no name lookups; -l: every packet at once; -M: keep memory flat.
        foreach (var argument in new[] { "-p", "-n", "-l", "-M", SessionResetPackets.ToString(System.Globalization.CultureInfo.InvariantCulture), "-T", "fields", "-E", "separator=/t", "-E", "occurrence=f" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var field in CapturedPacket.TsharkFields)
        {
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(field);
        }

        var aggregator = new TrafficAggregator(LocalAddresses(), _timeProvider.GetUtcNow());
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("TShark did not start.");

        _process = process;
        _aggregator = aggregator;
        _problem = null;

        _ = Task.Run(() => ReadPacketsAsync(process, aggregator));
        _ = Task.Run(() => ReadErrorsAsync(process));

        _logger.LogInformation("Traffic capture started on {Count} interface(s).", interfaces.Length);
    }

    private void Stop()
    {
        var process = _process;
        _process = null;
        _aggregator = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                // TShark runs dumpcap, which does the capturing; both have to go.
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(exception, "TShark could not be stopped.");
        }
        finally
        {
            process.Dispose();
        }

        _logger.LogInformation("Traffic capture stopped.");
    }

    private void ReportProblem(string problem)
    {
        if (_problem != problem)
        {
            _logger.LogWarning("{Problem}", problem);
        }

        _problem = problem;
        _aggregator = null;
    }

    private static async Task ReadPacketsAsync(Process process, TrafficAggregator aggregator)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (CapturedPacket.TryParse(line) is { } packet)
                {
                    aggregator.Add(packet);
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
            // The process was stopped while its output was being read.
        }
    }

    private async Task ReadErrorsAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                lock (_errorLines)
                {
                    _errorLines.Enqueue(line.Trim());

                    while (_errorLines.Count > MaxErrorLines)
                    {
                        _errorLines.Dequeue();
                    }
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
            // The process was stopped while its output was being read.
        }
    }

    private static string ListInterfaces(string tshark)
    {
        var startInfo = new ProcessStartInfo(tshark, "-D")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("TShark did not start.");
        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
        }

        return output;
    }

    /// <summary>Every address of this computer, so that each packet can be told as sent or received.</summary>
    private static IEnumerable<IPAddress> LocalAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Append(IPAddress.Loopback)
            .Append(IPAddress.IPv6Loopback);
    }

    [GeneratedRegex(@"\\Device\\NPF_(?<id>\{[0-9A-Fa-f-]+\})")]
    private static partial Regex NpcapDevice();
}
