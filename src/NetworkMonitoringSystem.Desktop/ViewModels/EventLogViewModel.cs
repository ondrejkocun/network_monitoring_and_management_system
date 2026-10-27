using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Commands;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>
/// The event log of the whole system for a chosen period: what happened on devices and who did what, such as
/// changed settings, removed devices, sign-ins and refused access. The filter only narrows what is shown.
/// </summary>
public sealed class EventLogViewModel : ViewModelBase
{
    public const string AllSeverities = "Všetky";

    private readonly IServerClient _server;
    private readonly ICsvExporter? _csvExporter;
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<EventLogRowViewModel> _allRows = [];
    private IReadOnlyList<EventLogRowViewModel> _rows = [];
    private HistoryPeriodOption _selectedPeriod;
    private string _filterText = string.Empty;
    private string _selectedSeverity = AllSeverities;
    private string _message = string.Empty;
    private bool _truncated;

    public EventLogViewModel(IServerClient server, TimeProvider? timeProvider = null, ICsvExporter? csvExporter = null)
    {
        ArgumentNullException.ThrowIfNull(server);

        _server = server;
        _csvExporter = csvExporter;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _selectedPeriod = Periods[1];
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _csvExporter is not null);
    }

    public IReadOnlyList<HistoryPeriodOption> Periods { get; } =
    [
        new("Posledná hodina", TimeSpan.FromHours(1)),
        new("Posledných 24 hodín", TimeSpan.FromHours(24)),
        new("Posledných 7 dní", TimeSpan.FromDays(7)),
        new("Posledných 30 dní", TimeSpan.FromDays(30)),
    ];

    public HistoryPeriodOption SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (value is not null && SetProperty(ref _selectedPeriod, value))
            {
                _ = LoadAsync();
            }
        }
    }

    public IReadOnlyList<string> SeverityOptions { get; } = [AllSeverities, "Informácia", "Varovanie", "Chyba"];

    public string SelectedSeverity
    {
        get => _selectedSeverity;
        set
        {
            if (SetProperty(ref _selectedSeverity, value ?? AllSeverities))
            {
                Apply();
            }
        }
    }

    /// <summary>Shows only events whose type, device, user or message contains this text, ignoring case.</summary>
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

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>The events that pass the filter, the newest first.</summary>
    public IReadOnlyList<EventLogRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>How many events are shown, and why some may be missing.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Exports the events as they are shown, filter included.</summary>
    private Task ExportAsync()
    {
        try
        {
            var path = _csvExporter?.Export(
                $"zaznam-udalosti-{_timeProvider.GetLocalNow():yyyy-MM-dd}",
                ["Čas", "Závažnosť", "Udalosť", "Zariadenie", "Používateľ", "Správa"],
                Rows.Select(row => (IReadOnlyList<string>)[row.OccurredText, row.SeverityText, row.TypeText, row.DeviceText, row.UserText, row.Message]).ToList());

            if (path is not null)
            {
                Message = $"Exportované do {path}.";
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            Message = $"Súbor sa nepodarilo uložiť: {exception.Message}";
        }

        return Task.CompletedTask;
    }

    public async Task LoadAsync()
    {
        var period = SelectedPeriod;
        var now = _timeProvider.GetUtcNow();

        try
        {
            var log = await _server.GetEventLogAsync(now - period.Length, now);

            // The answer may arrive after the user has chosen another period.
            if (SelectedPeriod != period)
            {
                return;
            }

            _allRows = log.Events.Select(entry => new EventLogRowViewModel(entry)).ToList();
            _truncated = log.Truncated;
            Apply();
        }
        catch (ServerClientException exception)
        {
            Message = exception.Kind switch
            {
                ServerErrorKind.Unavailable => "Server nie je dostupný.",
                ServerErrorKind.AccessDenied => "Na zobrazenie záznamu udalostí nemáte oprávnenie.",
                ServerErrorKind.NotSignedIn => "Prihlásenie vypršalo. Prihláste sa znova.",
                _ => exception.Message,
            };
        }
    }

    private void Apply()
    {
        var text = FilterText.Trim();

        Rows = _allRows
            .Where(row => SelectedSeverity == AllSeverities || row.SeverityText == SelectedSeverity)
            .Where(row => text.Length == 0
                || row.TypeText.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.DeviceText.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.UserText.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.Message.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var shown = Rows.Count == _allRows.Count ? $"Udalosti: {Rows.Count}." : $"Zobrazené udalosti: {Rows.Count} z {_allRows.Count}.";
        Message = _truncated
            ? $"{shown} Obdobie má viac udalostí, zobrazujú sa najnovšie; pre staršie zvoľte kratšie obdobie."
            : shown;
    }
}

public sealed class EventLogRowViewModel
{
    public EventLogRowViewModel(EventLogEntry entry)
    {
        OccurredText = DeviceHistoryViewModel.FormatTime(entry.OccurredAt.ToDateTimeOffset());
        SeverityText = EventRowViewModel.DescribeSeverity(entry.Severity);
        TypeText = EventRowViewModel.DescribeType(entry.Type);
        DeviceText = string.IsNullOrEmpty(entry.DeviceName) ? "–" : entry.DeviceName;
        UserText = string.IsNullOrEmpty(entry.UserName) ? "–" : entry.UserName;
        Message = ServerMessages.ToSlovakText(entry.Message);
    }

    public string OccurredText { get; }

    public string SeverityText { get; }

    public string TypeText { get; }

    public string DeviceText { get; }

    public string UserText { get; }

    public string Message { get; }
}
