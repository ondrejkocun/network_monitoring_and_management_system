using System.Globalization;
using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>
/// Who the selected device communicated with, the largest conversations first. The filter only narrows what is shown;
/// the agent keeps capturing everything, and the filter stays when new data arrives.
/// </summary>
public sealed class DeviceTrafficViewModel : ViewModelBase
{
    public const string AllTransports = "Všetky";

    private IReadOnlyList<TrafficRowViewModel> _allRows = [];
    private IReadOnlyList<TrafficRowViewModel> _rows = [];
    private string _filterText = string.Empty;
    private string _selectedTransport = AllTransports;
    private string _totalText = string.Empty;

    public DeviceTrafficViewModel()
    {
        Apply();
    }

    public DeviceTrafficViewModel(IReadOnlyList<TrafficEntry> entries)
    {
        Show(entries);
    }

    public IReadOnlyList<string> TransportOptions { get; } = [AllTransports, "TCP", "UDP", "ICMP", "OTHER"];

    /// <summary>Shows only rows whose address, port or protocol contains this text, ignoring case.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                Apply();
            }
        }
    }

    public string SelectedTransport
    {
        get => _selectedTransport;
        set
        {
            if (SetProperty(ref _selectedTransport, value ?? AllTransports))
            {
                Apply();
            }
        }
    }

    public bool IsFiltered => FilterText.Trim().Length > 0 || SelectedTransport != AllTransports;

    /// <summary>The rows that pass the filter.</summary>
    public IReadOnlyList<TrafficRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public string Header => IsFiltered ? $"Komunikácia ({Rows.Count} z {_allRows.Count})" : $"Komunikácia ({_allRows.Count})";

    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    /// <summary>Replaces the data with newly loaded entries; the filter stays as it is.</summary>
    public void Show(IReadOnlyList<TrafficEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _allRows = entries.Select(entry => new TrafficRowViewModel(entry)).ToList();
        Apply();
    }

    public void ClearFilter()
    {
        FilterText = string.Empty;
        SelectedTransport = AllTransports;
    }

    private void Apply()
    {
        var text = FilterText.Trim();

        Rows = _allRows
            .Where(row => SelectedTransport == AllTransports || row.Transport == SelectedTransport)
            .Where(row => text.Length == 0
                || row.RemoteAddress.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.Protocol.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.PortText == text)
            .ToList();

        var sent = (ulong)Rows.Sum(row => row.BytesSent);
        var received = (ulong)Rows.Sum(row => row.BytesReceived);

        TotalText = _allRows.Count == 0
            ? "V tomto období nebola zaznamenaná žiadna komunikácia."
            : Rows.Count == 0
                ? "Filtru nezodpovedá žiadna komunikácia."
                : $"Spolu odoslané {ByteSize.Format(sent)}, prijaté {ByteSize.Format(received)} v {Rows.Count} konverzáciách.";

        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsFiltered));
    }
}

public sealed class TrafficRowViewModel
{
    public TrafficRowViewModel(TrafficEntry entry)
    {
        RemoteAddress = entry.RemoteAddress;
        Protocol = entry.Protocol;
        Transport = entry.Transport.ToUpperInvariant();
        PortText = entry.HasServicePort ? entry.ServicePort.ToString(CultureInfo.InvariantCulture) : "–";
        BytesSent = Math.Max(0, entry.BytesSent);
        BytesReceived = Math.Max(0, entry.BytesReceived);
        SentText = $"{ByteSize.Format((ulong)BytesSent)} ({entry.PacketsSent} p.)";
        ReceivedText = $"{ByteSize.Format((ulong)BytesReceived)} ({entry.PacketsReceived} p.)";
    }

    public string RemoteAddress { get; }

    public string Protocol { get; }

    public string Transport { get; }

    public string PortText { get; }

    public long BytesSent { get; }

    public long BytesReceived { get; }

    public string SentText { get; }

    public string ReceivedText { get; }
}
