using System.Collections.ObjectModel;
using System.Globalization;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Commands;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IServerClient _server;
    private readonly IConfirmationDialog _confirmation;

    private string _statusMessage = "Pripájam sa k serveru…";
    private string _newDeviceName = string.Empty;
    private string _newDeviceIpAddress = string.Empty;
    private string _syncIntervalText = string.Empty;
    private string _syncIntervalHint = string.Empty;
    private bool _settingsLoaded;
    private DeviceRowViewModel? _selectedDevice;

    public MainWindowViewModel(IServerClient server, IConfirmationDialog confirmation)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(confirmation);

        _server = server;
        _confirmation = confirmation;

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
            if (SetProperty(ref _selectedDevice, value))
            {
                RemoveDeviceCommand.RaiseCanExecuteChanged();
            }
        }
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

            Devices.Clear();

            foreach (var device in devices)
            {
                Devices.Add(new DeviceRowViewModel(device));
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
