using System.Globalization;
using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>The latest system resources of one device, formatted for display.</summary>
public sealed class DeviceResourcesViewModel
{
    public DeviceResourcesViewModel(DeviceResourcesReply resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        RecordedAtText = resources.RecordedAt?.ToDateTimeOffset().ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "–";
        CpuText = resources.HasCpuUsagePercent ? ByteSize.FormatPercent(resources.CpuUsagePercent) : "–";
        MemoryText = ByteSize.FormatUsage(resources.MemoryUsedBytes, resources.MemoryTotalBytes);
        Disks = resources.Disks.Select(disk => new DiskRowViewModel(disk)).ToList();
        NetworkInterfaces = resources.NetworkInterfaces.Select(networkInterface => new NetworkInterfaceRowViewModel(networkInterface)).ToList();
    }

    public string RecordedAtText { get; }

    public string CpuText { get; }

    public string MemoryText { get; }

    public IReadOnlyList<DiskRowViewModel> Disks { get; }

    public IReadOnlyList<NetworkInterfaceRowViewModel> NetworkInterfaces { get; }
}

public sealed class DiskRowViewModel
{
    public DiskRowViewModel(DiskInfo disk)
    {
        Name = disk.Name;
        UsageText = ByteSize.FormatUsage(disk.TotalBytes - Math.Min(disk.FreeBytes, disk.TotalBytes), disk.TotalBytes);
        FreeText = ByteSize.Format(disk.FreeBytes);
    }

    public string Name { get; }

    public string UsageText { get; }

    public string FreeText { get; }
}

public sealed class NetworkInterfaceRowViewModel
{
    public NetworkInterfaceRowViewModel(NetworkInterfaceInfo networkInterface)
    {
        Name = networkInterface.Name;
        IpAddress = networkInterface.IpAddress;
        MacAddress = networkInterface.MacAddress;
        StateText = networkInterface.IsUp ? "Aktívne" : "Neaktívne";
        SentText = ByteSize.Format(networkInterface.BytesSent);
        ReceivedText = ByteSize.Format(networkInterface.BytesReceived);
    }

    public string Name { get; }

    public string IpAddress { get; }

    public string MacAddress { get; }

    public string StateText { get; }

    public string SentText { get; }

    public string ReceivedText { get; }
}

/// <summary>Formats sizes and shares the way the application shows them, independent of the system language.</summary>
public static class ByteSize
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("sk-SK");
    private static readonly string[] Units = ["B", "kB", "MB", "GB", "TB"];

    public static string Format(ulong bytes)
    {
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(Culture, $"{value:0.#} {Units[unit]}");
    }

    public static string FormatPercent(double percent) => string.Create(Culture, $"{percent:0.#} %");

    /// <summary>Formats used and total size with the share used, for example "7,9 GB z 15,8 GB (50 %)".</summary>
    public static string FormatUsage(ulong usedBytes, ulong totalBytes)
    {
        if (totalBytes == 0)
        {
            return "–";
        }

        return $"{Format(usedBytes)} z {Format(totalBytes)} ({FormatPercent(100.0 * usedBytes / totalBytes)})";
    }
}
