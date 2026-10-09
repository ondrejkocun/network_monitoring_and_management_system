using System.ComponentModel;
using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Metrics;

public interface ISystemActivityCollector
{
    /// <summary>Lists what runs on this computer now: processes, listening ports and connections.</summary>
    SystemActivity Collect();
}

/// <summary>Reads running processes through .NET and ports and connections through the Windows IP Helper API.</summary>
public sealed class WindowsSystemActivityCollector : ISystemActivityCollector
{
    private readonly Dictionary<(int Pid, long StartTicks), ProcessorSample> _previousSamples = [];

    public WindowsSystemActivityCollector()
    {
        // Takes the first reading now, so that the first report can already contain the load of processes.
        ReadProcesses();
    }

    public SystemActivity Collect()
    {
        var activity = new SystemActivity();

        activity.Processes.AddRange(ReadProcesses());
        IpHelperTables.ReadInto(activity);

        return activity;
    }

    private List<ProcessEntry> ReadProcesses()
    {
        var entries = new List<ProcessEntry>();
        var seen = new HashSet<(int, long)>();
        var now = Stopwatch.GetTimestamp();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                // Identifier 0 is the system idle "process", which only stands for unused processor time.
                if (process.Id == 0)
                {
                    continue;
                }

                try
                {
                    var entry = new ProcessEntry
                    {
                        Pid = (uint)process.Id,
                        Name = process.ProcessName,
                        MemoryBytes = (ulong)Math.Max(0, process.WorkingSet64),
                    };

                    if (TryReadTimes(process, out var startedAt, out var processorTime))
                    {
                        entry.StartedAt = Timestamp.FromDateTime(startedAt.ToUniversalTime());

                        var key = (process.Id, startedAt.Ticks);
                        seen.Add(key);

                        if (_previousSamples.TryGetValue(key, out var previous)
                            && ComputeUsagePercent(previous, new ProcessorSample(processorTime, now)) is { } usage)
                        {
                            entry.CpuUsagePercent = usage;
                        }

                        _previousSamples[key] = new ProcessorSample(processorTime, now);
                    }

                    entries.Add(entry);
                }
                catch (InvalidOperationException)
                {
                    // The process ended while it was being read.
                }
            }
        }

        // Samples of processes that no longer run are forgotten.
        foreach (var key in _previousSamples.Keys.Where(key => !seen.Contains(key)).ToList())
        {
            _previousSamples.Remove(key);
        }

        return entries;
    }

    /// <summary>
    /// Returns the share of all processors a process used between two samples, or null when no time has passed.
    /// </summary>
    public static double? ComputeUsagePercent(ProcessorSample earlier, ProcessorSample later)
    {
        return ComputeUsagePercent(earlier, later, Environment.ProcessorCount);
    }

    public static double? ComputeUsagePercent(ProcessorSample earlier, ProcessorSample later, int processorCount)
    {
        var elapsed = Stopwatch.GetElapsedTime(earlier.Timestamp, later.Timestamp);
        var used = later.ProcessorTime - earlier.ProcessorTime;

        if (elapsed <= TimeSpan.Zero || used < TimeSpan.Zero || processorCount <= 0)
        {
            return null;
        }

        return Math.Clamp(100.0 * used.TotalMilliseconds / (elapsed.TotalMilliseconds * processorCount), 0, 100);
    }

    /// <summary>Windows does not let an ordinary account read these values for protected system processes.</summary>
    private static bool TryReadTimes(Process process, out DateTime startedAt, out TimeSpan processorTime)
    {
        try
        {
            startedAt = process.StartTime;
            processorTime = process.TotalProcessorTime;

            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            startedAt = default;
            processorTime = default;

            return false;
        }
    }
}

/// <summary>Processor time a process had used at one moment.</summary>
/// <param name="ProcessorTime">Total processor time used by the process since it started.</param>
/// <param name="Timestamp">Moment of the reading, from <see cref="Stopwatch.GetTimestamp"/>.</param>
public readonly record struct ProcessorSample(TimeSpan ProcessorTime, long Timestamp);
