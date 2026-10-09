using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Metrics;

public interface ISystemMetricsCollector
{
    /// <summary>Measures the current use of system resources on this computer.</summary>
    SystemMetrics Collect();
}

/// <summary>Reads system resources through Windows APIs and the .NET base library.</summary>
public sealed class WindowsSystemMetricsCollector : ISystemMetricsCollector
{
    private readonly CpuUsageMeter _cpuMeter = new();

    public WindowsSystemMetricsCollector()
    {
        // Takes the first reading now, so that the first report can already contain the processor load.
        _cpuMeter.Measure();
    }

    public SystemMetrics Collect()
    {
        var metrics = new SystemMetrics();

        if (_cpuMeter.Measure() is { } cpuUsagePercent)
        {
            metrics.CpuUsagePercent = cpuUsagePercent;
        }

        var memory = ReadMemoryStatus();
        metrics.MemoryTotalBytes = memory.TotalPhysical;
        metrics.MemoryUsedBytes = memory.TotalPhysical - memory.AvailablePhysical;

        metrics.Disks.AddRange(ReadDisks());
        metrics.NetworkInterfaces.AddRange(ReadNetworkInterfaces());

        return metrics;
    }

    private static IEnumerable<DiskMetrics> ReadDisks()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            // Only local disks; a removable or network drive that is not ready would block or fail.
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }

            DiskMetrics disk;

            try
            {
                disk = new DiskMetrics
                {
                    Name = drive.Name,
                    TotalBytes = (ulong)drive.TotalSize,
                    FreeBytes = (ulong)drive.TotalFreeSpace,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The drive disappeared or cannot be read; the other drives are still reported.
                continue;
            }

            yield return disk;
        }
    }

    private static IEnumerable<NetworkInterfaceMetrics> ReadNetworkInterfaces()
    {
        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        var names = networkInterfaces.Select(networkInterface => networkInterface.Name).ToHashSet();

        foreach (var networkInterface in networkInterfaces)
        {
            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
                || IsDriverLayerOf(networkInterface.Name, names))
            {
                continue;
            }

            var statistics = networkInterface.GetIPStatistics();
            var ipv4Address = networkInterface.GetIPProperties().UnicastAddresses
                .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork);

            yield return new NetworkInterfaceMetrics
            {
                Name = networkInterface.Name,
                MacAddress = FormatMacAddress(networkInterface.GetPhysicalAddress()),
                IpAddress = ipv4Address?.Address.ToString() ?? string.Empty,
                IsUp = networkInterface.OperationalStatus == OperationalStatus.Up,
                BytesSent = (ulong)Math.Max(0, statistics.BytesSent),
                BytesReceived = (ulong)Math.Max(0, statistics.BytesReceived),
            };
        }
    }

    /// <summary>
    /// Windows lists every filter driver bound to an adapter as another interface, named after the adapter:
    /// "Ethernet" also appears as "Ethernet-QoS Packet Scheduler-0000" and similar. They repeat the adapter's
    /// own numbers, so only the adapter itself is reported.
    /// </summary>
    public static bool IsDriverLayerOf(string name, IReadOnlyCollection<string> allNames)
    {
        var lastDash = name.LastIndexOf('-');

        if (lastDash < 0 || name.Length - lastDash - 1 != 4 || !name[(lastDash + 1)..].All(char.IsAsciiDigit))
        {
            return false;
        }

        return allNames.Any(other => other.Length < name.Length && name.StartsWith(other + "-", StringComparison.Ordinal));
    }

    private static string FormatMacAddress(PhysicalAddress address)
    {
        return string.Join(':', address.GetAddressBytes().Select(part => part.ToString("X2")));
    }

    private static MemoryStatus ReadMemoryStatus()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return status;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    /// <summary>Layout of the Win32 MEMORYSTATUSEX structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
