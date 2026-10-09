using System.Collections.ObjectModel;
using System.Globalization;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Commands;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const string NoDeviceSelectedMessage = "Vyberte zariadenie v zozname.";

    private readonly IServerClient _server;
    private readonly IConfirmationDialog _confirmation;
    private readonly TimeProvider _timeProvider;

    // Reading a long history is much more work for the server than the current state, and it changes slowly.
    private static readonly TimeSpan HistoryReloadInterval = TimeSpan.FromSeconds(30);

    private string _statusMessage = "Pripájam sa k serveru…";
    private string _newDeviceName = string.Empty;
    private string _newDeviceIpAddress = string.Empty;
    private string _syncIntervalText = string.Empty;
    private string _syncIntervalHint = string.Empty;
    private bool _settingsLoaded;
    private DeviceRowViewModel? _selectedDevice;
    private DeviceResourcesViewModel? _selectedDeviceResources;
    private DeviceActivityViewModel? _selectedDeviceActivity;
    private DeviceResourcesReply? _shownResources;
    private DeviceActivityReply? _shownActivity;
    private string _resourcesMessage = NoDeviceSelectedMessage;
    private bool _isRebuildingList;
    private DeviceHistoryViewModel? _selectedDeviceHistory;
    private HistoryPeriodOption _selectedHistoryPeriod;
    private DateTimeOffset? _historyLoadedAt;

    /// <param name="timeProvider">The clock history periods are counted from; the system clock when not given.</param>
    public MainWindowViewModel(IServerClient server, IConfirmationDialog confirmation, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(confirmation);

        _server = server;
        _confirmation = confirmation;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _selectedHistoryPeriod = HistoryPeriods[1];

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        AddDeviceCommand = new AsyncRelayCommand(
            AddDeviceAsync,
            () => !string.IsNullOrWhiteSpace(NewDeviceName) && !string.IsNullOrWhiteSpace(NewDeviceIpAddress));
        RemoveDeviceCommand = new AsyncRelayCommand(RemoveSelectedDeviceAsync, () => SelectedDevice is not null);
        SaveSyncIntervalCommand = new AsyncRelayCommand(
            SaveSyncIntervalAsync,
            () => int.TryParse(SyncIntervalText, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    public string Title => "Network Monitoring & Management System";

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand AddDeviceCommand { get; }

    public AsyncRelayCommand RemoveDeviceCommand { get; }

    public AsyncRelayCommand SaveSyncIntervalCommand { get; }

    /// <summary>The device selected in the list, or null when none is.</summary>
    public DeviceRowViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            // While the list is being rebuilt the view reports that nothing is selected; that is not the user's choice.
            if (_isRebuildingList)
            {
                return;
            }

            var previousId = _selectedDevice?.Id;

            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            RemoveDeviceCommand.RaiseCanExecuteChanged();

            // Resources of another device must not stay on screen; those of the same device are simply reloaded.
            if (value?.Id != previousId)
            {
                SelectedDeviceResources = null;
                SelectedDeviceActivity = null;
                _shownResources = null;
                _shownActivity = null;
                SelectedDeviceHistory = null;
                _historyLoadedAt = null;
                ResourcesMessage = value is null ? NoDeviceSelectedMessage : "Načítavam…";
            }

            _ = LoadSelectedDeviceResourcesAsync();
        }
    }

    /// <summary>The latest system resources of the selected device, or null when there are none to show.</summary>
    public DeviceResourcesViewModel? SelectedDeviceResources
    {
        get => _selectedDeviceResources;
        private set => SetProperty(ref _selectedDeviceResources, value);
    }

    /// <summary>
    /// Processes, open ports and connections of the selected device, or null when its agent has reported none.
    /// </summary>
    public DeviceActivityViewModel? SelectedDeviceActivity
    {
        get => _selectedDeviceActivity;
        private set => SetProperty(ref _selectedDeviceActivity, value);
    }

    /// <summary>The periods of history the user can choose from.</summary>
    public IReadOnlyList<HistoryPeriodOption> HistoryPeriods { get; } =
    [
        new("Posledná hodina", TimeSpan.FromHours(1)),
        new("Posledných 24 hodín", TimeSpan.FromHours(24)),
        new("Posledných 7 dní", TimeSpan.FromDays(7)),
        new("Posledných 30 dní", TimeSpan.FromDays(30)),
    ];

    /// <summary>The period the history of the selected device is shown for; it ends now.</summary>
    public HistoryPeriodOption SelectedHistoryPeriod
    {
        get => _selectedHistoryPeriod;
        set
        {
            // The view reports no selection while it is being built; a period is always chosen.
            if (value is null || !SetProperty(ref _selectedHistoryPeriod, value))
            {
                return;
            }

            _historyLoadedAt = null;
            _ = LoadSelectedDeviceHistoryAsync();
        }
    }

    /// <summary>Availability, outages, events and resource use of the selected device, or null when not loaded.</summary>
    public DeviceHistoryViewModel? SelectedDeviceHistory
    {
        get => _selectedDeviceHistory;
        private set => SetProperty(ref _selectedDeviceHistory, value);
    }

    /// <summary>
    /// Loads the history of the selected device for the selected period. Does nothing when it was loaded
    /// a moment ago, unless the device or the period has changed since.
    /// </summary>
    public async Task LoadSelectedDeviceHistoryAsync()
    {
        var device = SelectedDevice;
        var period = SelectedHistoryPeriod;
        var now = _timeProvider.GetUtcNow();

        if (device is null || now - _historyLoadedAt < HistoryReloadInterval)
        {
            return;
        }

        try
        {
            var from = now - period.Length;
            var history = await _server.GetDeviceHistoryAsync(device.Id, from, now);

            // The answer may arrive after the user has selected another device or period.
            if (SelectedDevice?.Id != device.Id || SelectedHistoryPeriod != period)
            {
                return;
            }

            _historyLoadedAt = now;
            SelectedDeviceHistory = new DeviceHistoryViewModel(history, from, now, SelectedDeviceHistory);
        }
        catch (ServerClientException)
        {
            // What is on screen stays; the status bar already says when the server cannot be reached.
        }
    }

    /// <summary>Explains why no resources are shown; empty when they are.</summary>
    public string ResourcesMessage
    {
        get => _resourcesMessage;
        private set => SetProperty(ref _resourcesMessage, value);
    }

    /// <summary>Loads the latest system resources of the selected device from the server.</summary>
    public async Task LoadSelectedDeviceResourcesAsync()
    {
        var device = SelectedDevice;

        if (device is null)
        {
            return;
        }

        try
        {
            var resources = await _server.GetDeviceResourcesAsync(device.Id);
            var activity = await _server.GetDeviceActivityAsync(device.Id);

            // The answer may arrive after the user has selected another device.
            if (SelectedDevice?.Id != device.Id)
            {
                return;
            }

            // Unchanged data keep their view models, so the lists on screen do not jump back to the top
            // every time the device list is refreshed.
            if (!resources.Equals(_shownResources))
            {
                _shownResources = resources;
                SelectedDeviceResources = resources.HasData ? new DeviceResourcesViewModel(resources) : null;
            }

            if (!activity.Equals(_shownActivity))
            {
                _shownActivity = activity;

                var activityViewModel = new DeviceActivityViewModel(activity);
                SelectedDeviceActivity = activityViewModel.IsEmpty ? null : activityViewModel;
            }

            ResourcesMessage = resources.HasData
                ? string.Empty
                : "Zariadenie zatiaľ neposlalo žiadne údaje o prostriedkoch. Posiela ich len zariadenie s agentom.";
        }
        catch (ServerClientException exception)
        {
            if (SelectedDevice?.Id == device.Id && SelectedDeviceResources is null)
            {
                ResourcesMessage = Describe(exception);
            }

            return;
        }

        await LoadSelectedDeviceHistoryAsync();
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string NewDeviceName
    {
        get => _newDeviceName;
        set
        {
            if (SetProperty(ref _newDeviceName, value))
            {
                AddDeviceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewDeviceIpAddress
    {
        get => _newDeviceIpAddress;
        set
        {
            if (SetProperty(ref _newDeviceIpAddress, value))
            {
                AddDeviceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The global synchronization interval in seconds, as typed by the user.</summary>
    public string SyncIntervalText
    {
        get => _syncIntervalText;
        set
        {
            if (SetProperty(ref _syncIntervalText, value))
            {
                SaveSyncIntervalCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SyncIntervalHint
    {
        get => _syncIntervalHint;
        private set => SetProperty(ref _syncIntervalHint, value);
    }

    /// <summary>Loads the current device list from the server. Called at start-up, periodically and on demand.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var devices = await _server.GetDevicesAsync();

            // The list is rebuilt, so the selection is carried over to the row of the same device.
            var selectedId = SelectedDevice?.Id;

            _isRebuildingList = true;

            try
            {
                Devices.Clear();

                foreach (var device in devices)
                {
                    Devices.Add(new DeviceRowViewModel(device));
                }
            }
            finally
            {
                _isRebuildingList = false;
            }

            SelectedDevice = selectedId is null ? null : Devices.FirstOrDefault(device => device.Id == selectedId);

            // The interval is loaded only once, so a periodic refresh does not overwrite what the user is typing.
            if (!_settingsLoaded)
            {
                ShowSettings(await _server.GetSettingsAsync());
            }

            var online = Devices.Count(device => device.Status == DeviceStatus.Online);
            StatusMessage = $"Pripojené k serveru. Zariadenia: {Devices.Count}, z toho online: {online}.";
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);
        }
    }

    private async Task AddDeviceAsync()
    {
        try
        {
            await _server.AddAgentlessDeviceAsync(NewDeviceName.Trim(), NewDeviceIpAddress.Trim());
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);

            return;
        }

        var addedName = NewDeviceName.Trim();
        NewDeviceName = string.Empty;
        NewDeviceIpAddress = string.Empty;

        await RefreshAsync();

        StatusMessage = $"Zariadenie „{addedName}“ bolo pridané. {StatusMessage}";
    }

    private async Task RemoveSelectedDeviceAsync()
    {
        var device = SelectedDevice;

        if (device is null)
        {
            return;
        }

        var confirmed = _confirmation.Confirm(
            "Odstrániť zariadenie",
            $"Naozaj chcete odstrániť zariadenie „{device.Name}“? Odstráni sa aj jeho história stavov a výpadkov.");

        if (!confirmed)
        {
            return;
        }

        try
        {
            await _server.RemoveDeviceAsync(device.Id);
        }
        catch (ServerClientException exception) when (exception.Kind != ServerErrorKind.NotFound)
        {
            StatusMessage = Describe(exception);

            return;
        }
        catch (ServerClientException)
        {
            // Someone else has already removed the device; the list only needs to catch up.
        }

        SelectedDevice = null;

        await RefreshAsync();

        StatusMessage = $"Zariadenie „{device.Name}“ bolo odstránené. {StatusMessage}";
    }

    private async Task SaveSyncIntervalAsync()
    {
        var seconds = int.Parse(SyncIntervalText, NumberStyles.None, CultureInfo.InvariantCulture);

        try
        {
            ShowSettings(await _server.UpdateSyncIntervalAsync(seconds));
            StatusMessage = $"Interval synchronizácie je nastavený na {seconds} s.";
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);
        }
    }

    private void ShowSettings(MonitoringSettingsInfo settings)
    {
        _settingsLoaded = true;
        SyncIntervalText = settings.SyncIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        SyncIntervalHint =
            $"Povolený rozsah: {settings.MinSyncIntervalSeconds} – {settings.MaxSyncIntervalSeconds} s. "
            + $"Zariadenie je offline po {settings.OfflineAfterMissedSyncs} vynechaných intervaloch.";
    }

    private static string Describe(ServerClientException exception)
    {
        return exception.Kind switch
        {
            ServerErrorKind.Unavailable => "Server nie je dostupný.",
            ServerErrorKind.AccessDenied => "Server odmietol prístup.",
            ServerErrorKind.InvalidInput => $"Server odmietol zadané údaje: {exception.Message}",
            _ => $"Chyba servera: {exception.Message}",
        };
    }
}
