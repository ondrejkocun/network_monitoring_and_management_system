using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NetworkMonitoringSystem.Agent.Metrics;

/// <summary>Processor time counters of the whole system since it started, in 100 ns units.</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User)
{
    /// <summary>
    /// Returns the processor load between two readings in percent, or null when no time has passed.
    /// Windows counts idle time as part of kernel time, so the total is kernel plus user.
    /// </summary>
    public static double? UsagePercentBetween(CpuTimes earlier, CpuTimes later)
    {
        if (later.Idle < earlier.Idle || later.Kernel < earlier.Kernel || later.User < earlier.User)
        {
            return null;
        }

        var idle = later.Idle - earlier.Idle;
        var total = (later.Kernel - earlier.Kernel) + (later.User - earlier.User);

        if (total == 0)
        {
            return null;
        }

        return Math.Clamp(100.0 * (1.0 - ((double)idle / total)), 0, 100);
    }
}

/// <summary>
/// Measures the average processor load between consecutive calls. The first call has nothing to compare with
/// and returns null.
/// </summary>
public sealed class CpuUsageMeter
{
    private readonly Func<CpuTimes> _readTimes;
    private CpuTimes? _previous;

    public CpuUsageMeter()
        : this(ReadSystemTimes)
    {
    }

    public CpuUsageMeter(Func<CpuTimes> readTimes)
    {
        _readTimes = readTimes;
    }

    public double? Measure()
    {
        var current = _readTimes();
        var usage = _previous is { } previous ? CpuTimes.UsagePercentBetween(previous, current) : null;

        _previous = current;

        return usage;
    }

    private static CpuTimes ReadSystemTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new CpuTimes(idle, kernel, user);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);
}
