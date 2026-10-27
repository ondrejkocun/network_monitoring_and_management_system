using System.IO;
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
    private readonly IFilePicker? _filePicker;
    private readonly ICsvExporter? _csvExporter;
    private readonly ITextInputDialog? _textInput;

    // Reading a long history is much more work for the server than the current state, and it changes slowly.
    private static readonly TimeSpan HistoryReloadInterval = TimeSpan.FromSeconds(30);

    private string _statusMessage = "Pripájam sa k serveru…";
    private string _newDeviceName = string.Empty;
    private string _newDeviceIpAddress = string.Empty;
    private string _newDeviceTcpPort = string.Empty;
    private string _syncIntervalText = string.Empty;
    private string _offlineThresholdText = string.Empty;
    private string _retentionDaysText = string.Empty;
    private string _settingsHint = string.Empty;
    private MonitoringSettingsInfo? _shownSettings;
    private bool _settingsLoaded;
    private DeviceRowViewModel? _selectedDevice;
    private DeviceResourcesViewModel? _selectedDeviceResources;
    private DeviceActivityViewModel? _selectedDeviceActivity;
    private DeviceResourcesReply? _shownResources;
    private DeviceActivityReply? _shownActivity;
    private IReadOnlyList<CommandInfo>? _shownCommands;
    private IReadOnlyList<CommandRowViewModel> _deviceCommands = [];
    private CommandRowViewModel? _selectedCommandRow;
    private CommandOption _selectedCommand = CommandOption.All[0];
    private string _commandParameter = string.Empty;
    private string _commandMessage = string.Empty;
    private string _resourcesMessage = NoDeviceSelectedMessage;
    private bool _isRebuildingList;
    private bool _sessionEnded;
    private readonly Dictionary<string, (string Name, DateTimeOffset Since)> _alerts = [];
    private List<DeviceRowViewModel> _allDevices = [];
    private NetworkOverviewViewModel? _networkOverview;
    private EventLogReply? _overviewWarnings;
    private string _deviceFilterText = string.Empty;
    private string _deviceStatusFilter = AllStatuses;
    private DateTimeOffset? _lastRefreshedAt;
    private bool _isServerUnavailable;
    private string _addDeviceMessage = string.Empty;
    private string _settingsMessage = string.Empty;
    private HashSet<string> _permissions = [];
    private string _currentUserName = string.Empty;
    private DeviceHistoryViewModel? _selectedDeviceHistory;
    private DeviceTrafficViewModel? _selectedDeviceTraffic;
    private DeviceActivityHistoryViewModel? _selectedDeviceActivityHistory;
    private DateTimeOffset? _trafficSince;
    private HistoryPeriodOption _selectedHistoryPeriod;
    private DateTimeOffset? _historyLoadedAt;

    /// <param name="timeProvider">The clock history periods are counted from; the system clock when not given.</param>
    /// <param name="filePicker">Lets the user choose a file to send to a device; without it files cannot be sent.</param>
    /// <param name="textInput">Asks the user for a new name of a device; without it devices cannot be renamed.</param>
    /// <param name="csvExporter">Saves tables as CSV files; without it nothing can be exported.</param>
    public MainWindowViewModel(
        IServerClient server,
        IConfirmationDialog confirmation,
        TimeProvider? timeProvider = null,
        IFilePicker? filePicker = null,
        ITextInputDialog? textInput = null,
        ICsvExporter? csvExporter = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(confirmation);

        _server = server;
        _confirmation = confirmation;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _filePicker = filePicker;
        _csvExporter = csvExporter;
        _textInput = textInput;
        RenameDeviceCommand = new AsyncRelayCommand(RenameSelectedDeviceAsync, () => SelectedDevice is { CanManage: true } && _textInput is not null);
        SendFileCommand = new AsyncRelayCommand(SendFileAsync, () => CanSendFile && _filePicker is not null);
        _selectedHistoryPeriod = HistoryPeriods[1];

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        AddDeviceCommand = new AsyncRelayCommand(
            AddDeviceAsync,
            () => !string.IsNullOrWhiteSpace(NewDeviceName)
                && !string.IsNullOrWhiteSpace(NewDeviceIpAddress)
                && TryParseTcpPort(NewDeviceTcpPort, out _));
        RemoveDeviceCommand = new AsyncRelayCommand(RemoveSelectedDeviceAsync, () => SelectedDevice is { CanManage: true });
        ToggleDeviceEnabledCommand = new AsyncRelayCommand(ToggleSelectedDeviceEnabledAsync, () => SelectedDevice is { CanManage: true });
        ToggleTrafficCaptureCommand = new AsyncRelayCommand(ToggleSelectedDeviceTrafficCaptureAsync, () => SelectedDevice is { HasAgent: true, CanManage: true });
        ResetTrafficCommand = new AsyncRelayCommand(ResetTrafficAsync, () => SelectedDevice is not null);
        ShowAllTrafficCommand = new AsyncRelayCommand(ShowAllTrafficAsync, () => _trafficSince is not null);
        SaveSettingsCommand = new AsyncRelayCommand(
            SaveSettingsAsync,
            () => IsWholeNumber(SyncIntervalText) && IsWholeNumber(OfflineThresholdText) && IsWholeNumber(RetentionDaysText));
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
        DismissAlertsCommand = new AsyncRelayCommand(DismissAlertsAsync);
        ExportTrafficCommand = new AsyncRelayCommand(ExportTrafficAsync, () => _csvExporter is not null);
        ExportEventsCommand = new AsyncRelayCommand(ExportEventsAsync, () => _csvExporter is not null);
        ExportOutagesCommand = new AsyncRelayCommand(ExportOutagesAsync, () => _csvExporter is not null);
        SendCommandCommand = new AsyncRelayCommand(
            SendCommandAsync,
            () => CanSendCommands && (!SelectedCommand.NeedsParameter || !string.IsNullOrWhiteSpace(CommandParameter)));
    }

    public AsyncRelayCommand SendCommandCommand { get; }

    /// <summary>
    /// The remote commands the user holds a permission for on the selected device. The server still checks each one.
    /// </summary>
    public IReadOnlyList<CommandOption> CommandOptions { get; private set; } = CommandOption.All;

    public CommandOption SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            // The view reports no selection while it is being built; a command is always chosen.
            if (value is null || !SetProperty(ref _selectedCommand, value))
            {
                return;
            }

            // A process identifier must not be sent as a service name by mistake.
            CommandParameter = string.Empty;
            OnPropertyChanged(nameof(CommandParameterLabel));
            OnPropertyChanged(nameof(IsCommandParameterNeeded));
            SendCommandCommand.RaiseCanExecuteChanged();
        }
    }

    public string CommandParameterLabel => SelectedCommand.ParameterLabel ?? string.Empty;

    public bool IsCommandParameterNeeded => SelectedCommand.NeedsParameter;

    public string CommandParameter
    {
        get => _commandParameter;
        set
        {
            if (SetProperty(ref _commandParameter, value))
            {
                SendCommandCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when the selected device has an agent that could carry a command out and the user may send one.</summary>
    public bool CanSendCommands => SelectedDevice is { HasAgent: true } && CommandOptions.Count > 0;

    /// <summary>True when the selected device has an agent and the user may send it a file.</summary>
    public bool CanSendFile => SelectedDevice is { HasAgent: true } device && device.Can(CommandOption.SendFileName);

    /// <summary>Says what happened to the last command sent, or why commands cannot be sent.</summary>
    public string CommandMessage
    {
        get => _commandMessage;
        private set => SetProperty(ref _commandMessage, value);
    }

    /// <summary>The most recent commands sent to the selected device, the newest first.</summary>
    public IReadOnlyList<CommandRowViewModel> DeviceCommands
    {
        get => _deviceCommands;
        private set
        {
            if (SetProperty(ref _deviceCommands, value))
            {
                OnPropertyChanged(nameof(CommandsHeader));
            }
        }
    }

    public string CommandsHeader => DeviceCommands.Count == 0 ? "Príkazy" : $"Príkazy ({DeviceCommands.Count})";

    /// <summary>The command whose whole result is shown under the list.</summary>
    public CommandRowViewModel? SelectedCommandRow
    {
        get => _selectedCommandRow;
        set => SetProperty(ref _selectedCommandRow, value);
    }

    /// <summary>Loads the commands sent to the selected device from the server.</summary>
    public async Task LoadSelectedDeviceCommandsAsync()
    {
        var device = SelectedDevice;

        if (device is null)
        {
            return;
        }

        try
        {
            var commands = await _server.GetDeviceCommandsAsync(device.Id);

            // The answer may arrive after the user has selected another device.
            if (SelectedDevice?.Id != device.Id || (_shownCommands is not null && commands.SequenceEqual(_shownCommands)))
            {
                return;
            }

            // The list is rebuilt, so the selection is carried over to the row of the same command.
            var selectedId = SelectedCommandRow?.Id;

            _shownCommands = commands;
            DeviceCommands = commands.Select(command => new CommandRowViewModel(command)).ToList();
            SelectedCommandRow = DeviceCommands.FirstOrDefault(command => command.Id == selectedId);
        }
        catch (ServerClientException)
        {
            // What is on screen stays; the status bar already says when the server cannot be reached.
        }
    }

    /// <summary>Lets the user pick a file and sends it into the inbox folder of the selected device.</summary>
    public AsyncRelayCommand SendFileCommand { get; }

    /// <summary>Largest file the server accepts; checked here too, so the user is not left waiting for a refusal.</summary>
    public const long MaxFileSize = 100L * 1024 * 1024;

    private async Task SendFileAsync()
    {
        var device = SelectedDevice;

        if (device is not { HasAgent: true } || _filePicker?.PickFileToSend() is not { } path)
        {
            return;
        }

        var fileName = Path.GetFileName(path);

        try
        {
            var size = new FileInfo(path).Length;

            if (size > MaxFileSize)
            {
                CommandMessage = $"Súbor „{fileName}“ má {ByteSize.Format((ulong)size)}; poslať možno najviac 100 MB.";

                return;
            }

            if (!_confirmation.Confirm(
                    "Poslať súbor",
                    $"Naozaj chcete zariadeniu „{device.Name}“ poslať súbor „{fileName}“ ({ByteSize.Format((ulong)size)})? "
                    + "Agent ho uloží do priečinka C:\\ProgramData\\NetworkMonitoringSystem\\Inbox a nespustí ho."))
            {
                return;
            }

            CommandMessage = $"Posielam súbor „{fileName}“…";

            await using var content = File.OpenRead(path);
            await _server.SendFileAsync(device.Id, fileName, content);
        }
        catch (ServerClientException exception)
        {
            CommandMessage = Describe(exception);

            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CommandMessage = $"Súbor „{fileName}“ sa nedá prečítať: {exception.Message}";

            return;
        }

        CommandMessage = $"Súbor „{fileName}“ bol odoslaný na server. Agent si ho stiahne pri najbližšom dopyte na príkazy.";

        await LoadSelectedDeviceCommandsAsync();
        SelectedCommandRow = DeviceCommands.FirstOrDefault();
    }

    private async Task SendCommandAsync()
    {
        var device = SelectedDevice;
        var command = SelectedCommand;

        if (device is null)
        {
            return;
        }

        if (command.Warning is not null && !_confirmation.Confirm(
                command.Label,
                $"Naozaj chcete zariadeniu „{device.Name}“ poslať príkaz „{command.Label}“? {command.Warning}"))
        {
            return;
        }

        try
        {
            await _server.SendCommandAsync(device.Id, command.Name, command.NeedsParameter ? CommandParameter.Trim() : string.Empty);
        }
        catch (ServerClientException exception)
        {
            CommandMessage = Describe(exception);

            return;
        }

        CommandMessage = $"Príkaz „{command.Label}“ bol odoslaný. Výsledok sa zobrazí v zozname, keď ho agent vykoná.";

        await LoadSelectedDeviceCommandsAsync();

        // The command just sent is the first in the list; showing it saves the user a click.
        SelectedCommandRow = DeviceCommands.FirstOrDefault();
    }

    /// <summary>
    /// Raised when the user signs out or the server no longer accepts the session. The window closes
    /// and the user is asked to sign in again; <see cref="SessionEndMessage"/> says why, when there is a reason.
    /// </summary>
    public event EventHandler? SessionEnded;

    public AsyncRelayCommand LogoutCommand { get; }

    /// <summary>Name of the signed-in user, shown in the window.</summary>
    public string CurrentUserName
    {
        get => _currentUserName;
        set
        {
            if (SetProperty(ref _currentUserName, value))
            {
                OnPropertyChanged(nameof(CurrentUserText));
            }
        }
    }

    /// <summary>True when the user may add devices; shown controls follow what the server would allow anyway.</summary>
    public bool CanAddDevices => _permissions.Contains("ManageDevices");

    public bool CanManageSettings => _permissions.Contains("ManageSettings");

    public bool CanManageUsers => _permissions.Contains("ManageUsers");

    public bool CanViewEventLog => _permissions.Contains("ViewEventLog");

    /// <summary>Takes over the name and the permissions of the user who has just signed in.</summary>
    public void ApplySession(LoginReply session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _permissions = session.Permissions.ToHashSet(StringComparer.Ordinal);
        CurrentUserName = session.UserName;

        OnPropertyChanged(nameof(CanAddDevices));
        OnPropertyChanged(nameof(CanManageEnrollmentCodes));
        OnPropertyChanged(nameof(CanManageSettings));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(CanViewEventLog));
    }

    public AccessViewModel CreateAccessViewModel() => new(_server, _confirmation);

    public ChangePasswordViewModel CreateChangePasswordViewModel() => new(_server);

    public EventLogViewModel CreateEventLogViewModel() => new(_server, _timeProvider, _csvExporter);

    /// <param name="deviceId">The device to offer first, when the window is opened for one.</param>
    public EnrollmentCodesViewModel CreateEnrollmentCodesViewModel(string? deviceId = null) => new(_server, _confirmation, _allDevices, CanAddDevices, deviceId);

    /// <summary>True when the user may create a code: for a new computer, or for a device with an agent they manage.</summary>
    public bool CanManageEnrollmentCodes => CanAddDevices || _allDevices.Any(device => device.HasAgent && device.CanManage);

    /// <summary>Changing the password signs the user out everywhere, including here.</summary>
    public void EndSessionAfterPasswordChange()
    {
        EndSession("Heslo bolo zmenené. Prihláste sa novým heslom.");
    }

    public string CurrentUserText => string.IsNullOrEmpty(CurrentUserName) ? string.Empty : $"Prihlásený: {CurrentUserName}";

    /// <summary>What to tell the user at the next sign-in; empty when they signed out themselves.</summary>
    public string SessionEndMessage { get; private set; } = string.Empty;

    private async Task LogoutAsync()
    {
        await _server.LogoutAsync();

        EndSession(string.Empty);
    }

    private void EndSession(string message)
    {
        if (_sessionEnded)
        {
            return;
        }

        _sessionEnded = true;
        SessionEndMessage = message;
        SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    public string Title => "Network Monitoring & Management System";

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = [];

    public const string AllStatuses = "Všetky";

    public IReadOnlyList<string> DeviceStatusFilters { get; } = [AllStatuses, "Online", "Offline", "Zakázané"];

    /// <summary>Shows only devices whose name, address or operating system contains the text.</summary>
    public string DeviceFilterText
    {
        get => _deviceFilterText;
        set
        {
            if (SetProperty(ref _deviceFilterText, value ?? string.Empty))
            {
                ShowFilteredDevices(SelectedDevice?.Id);
            }
        }
    }

    /// <summary>Shows only devices in this state; "Zakázané" are the disabled ones, which the other states leave out.</summary>
    public string DeviceStatusFilter
    {
        get => _deviceStatusFilter;
        set
        {
            if (value is not null && SetProperty(ref _deviceStatusFilter, value))
            {
                ShowFilteredDevices(SelectedDevice?.Id);
            }
        }
    }

    public bool IsDeviceListFiltered => DeviceFilterText.Trim().Length > 0 || DeviceStatusFilter != AllStatuses;

    /// <summary>Puts the devices that pass the filter into the list and keeps the selection when it is still there.</summary>
    private void ShowFilteredDevices(string? selectedId)
    {
        var text = DeviceFilterText.Trim();

        _isRebuildingList = true;

        try
        {
            Devices.Clear();

            foreach (var device in _allDevices.Where(device => PassesFilter(device, text)))
            {
                Devices.Add(device);
            }
        }
        finally
        {
            _isRebuildingList = false;
        }

        SelectedDevice = selectedId is null ? null : Devices.FirstOrDefault(device => device.Id == selectedId);
        OnPropertyChanged(nameof(IsDeviceListFiltered));
        OnPropertyChanged(nameof(DevicesSummaryText));
    }

    private bool PassesFilter(DeviceRowViewModel device, string text)
    {
        var statusMatches = DeviceStatusFilter switch
        {
            "Online" => device.IsEnabled && device.Status == DeviceStatus.Online,
            "Offline" => device.IsEnabled && device.Status == DeviceStatus.Offline,
            "Zakázané" => !device.IsEnabled,
            _ => true,
        };

        return statusMatches
            && (text.Length == 0
                || device.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || device.Address.Contains(text, StringComparison.OrdinalIgnoreCase)
                || device.OperatingSystem.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>How many devices there are and in what state, for example "3 zariadenia · 2 online · 1 offline".</summary>
    public string DevicesSummaryText
    {
        get
        {
            var parts = new List<string> { CountDevices(_allDevices.Count) };

            if (IsDeviceListFiltered)
            {
                parts[0] += $" (zobrazené {Devices.Count})";
            }
            var online = _allDevices.Count(device => device.IsEnabled && device.Status == DeviceStatus.Online);
            var offline = _allDevices.Count(device => device.IsEnabled && device.Status == DeviceStatus.Offline);
            var disabled = _allDevices.Count(device => !device.IsEnabled);

            parts.Add($"{online} online");

            if (offline > 0)
            {
                parts.Add($"{offline} offline");
            }

            if (disabled > 0)
            {
                parts.Add($"{disabled} zakázané");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Name, state and address of the selected device, shown above its details.</summary>
    public string SelectedDeviceHeaderText => SelectedDevice is { } device
        ? string.Join(" · ", new[] { device.Name, device.StatusText, device.Address }.Where(part => !string.IsNullOrEmpty(part)))
        : "Prehľad siete";

    private static string CountDevices(int count) => count switch
    {
        1 => "1 zariadenie",
        >= 2 and <= 4 => $"{count} zariadenia",
        _ => $"{count} zariadení",
    };

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand AddDeviceCommand { get; }

    public AsyncRelayCommand RemoveDeviceCommand { get; }

    /// <summary>Disables the selected device, or enables it when it is disabled.</summary>
    public AsyncRelayCommand ToggleDeviceEnabledCommand { get; }

    public string ToggleDeviceEnabledText => SelectedDevice is { IsEnabled: false } ? "Povoliť vybrané" : "Zakázať vybrané";

    /// <summary>Saves the monitoring settings the user changed: interval, offline tolerance and history retention.</summary>
    public AsyncRelayCommand SaveSettingsCommand { get; }

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
            ToggleDeviceEnabledCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ToggleDeviceEnabledText));
            RenameDeviceCommand.RaiseCanExecuteChanged();
            ToggleTrafficCaptureCommand.RaiseCanExecuteChanged();
            ResetTrafficCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ToggleTrafficCaptureText));
            OnPropertyChanged(nameof(TrafficCaptureStatusText));
            OnPropertyChanged(nameof(SelectedDeviceHeaderText));
            OnPropertyChanged(nameof(HasSelectedDevice));

            if (value is null && !_isRebuildingList)
            {
                ShowOverview();
            }
            UpdateCommandOptions();

            // Resources of another device must not stay on screen; those of the same device are simply reloaded.
            if (value?.Id != previousId)
            {
                SelectedDeviceResources = null;
                SelectedDeviceActivity = null;
                _shownResources = null;
                _shownActivity = null;
                _shownCommands = null;
                DeviceCommands = [];
                SelectedCommandRow = null;
                CommandMessage = value switch
                {
                    { HasAgent: false } => "Príkazy možno posielať len zariadeniu s agentom.",
                    { } when !CanSendCommands && !CanSendFile => "Na príkazy pre toto zariadenie nemáte oprávnenie.",
                    _ => string.Empty,
                };
                SelectedDeviceHistory = null;
                SelectedDeviceTraffic = null;
                SelectedDeviceActivityHistory = null;
                SetTrafficSince(null);
                _historyLoadedAt = null;
                ResourcesMessage = value is null ? NoDeviceSelectedMessage : "Načítavam…";
            }

            _ = LoadSelectedDeviceResourcesAsync();
        }
    }

    /// <summary>Offers the commands the user may send to the selected device; the list is replaced only when it changes.</summary>
    private void UpdateCommandOptions()
    {
        var options = SelectedDevice is { } device
            ? CommandOption.All.Where(option => device.Can(option.Name)).ToList()
            : CommandOption.All;

        if (!options.SequenceEqual(CommandOptions))
        {
            CommandOptions = options;
            OnPropertyChanged(nameof(CommandOptions));

            if (options.Count > 0 && !options.Contains(SelectedCommand))
            {
                SelectedCommand = options[0];
            }
        }

        OnPropertyChanged(nameof(CanSendCommands));
        OnPropertyChanged(nameof(CanSendFile));
        SendCommandCommand.RaiseCanExecuteChanged();
        SendFileCommand.RaiseCanExecuteChanged();
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

            var activityHistory = await _server.GetDeviceActivityHistoryAsync(device.Id, from, now);

            if (SelectedDevice?.Id == device.Id && SelectedHistoryPeriod == period)
            {
                // The same view model is kept, so a filter the user typed stays while new data arrives.
                if (SelectedDeviceActivityHistory is null)
                {
                    SelectedDeviceActivityHistory = new DeviceActivityHistoryViewModel(activityHistory, now);
                }
                else
                {
                    SelectedDeviceActivityHistory.Show(activityHistory, now);
                }
            }
        }
        catch (ServerClientException)
        {
            // What is on screen stays; the status bar already says when the server cannot be reached.
            return;
        }

        await LoadSelectedDeviceTrafficAsync(now);
    }

    /// <summary>
    /// Loads the traffic of the selected device for the selected period, or only since the table was reset.
    /// </summary>
    public async Task LoadSelectedDeviceTrafficAsync(DateTimeOffset now)
    {
        var device = SelectedDevice;
        var period = SelectedHistoryPeriod;
        var since = _trafficSince;

        if (device is null)
        {
            return;
        }

        var from = since is { } resetAt && resetAt > now - period.Length ? resetAt : now - period.Length;

        try
        {
            var traffic = from < now ? await _server.GetDeviceTrafficAsync(device.Id, from, now) : [];

            // The answer may arrive after the user has selected another device or period, or reset the table again.
            if (SelectedDevice?.Id != device.Id || SelectedHistoryPeriod != period || _trafficSince != since)
            {
                return;
            }

            // The same view model is kept, so a filter the user typed stays while new data arrives.
            if (SelectedDeviceTraffic is null)
            {
                SelectedDeviceTraffic = new DeviceTrafficViewModel(traffic);
            }
            else
            {
                SelectedDeviceTraffic.Show(traffic);
            }
        }
        catch (ServerClientException)
        {
            // What is on screen stays; the status bar already says when the server cannot be reached.
        }
    }

    /// <summary>Ports and processes of the selected device in the selected period, or null when not loaded.</summary>
    public DeviceActivityHistoryViewModel? SelectedDeviceActivityHistory
    {
        get => _selectedDeviceActivityHistory;
        private set => SetProperty(ref _selectedDeviceActivityHistory, value);
    }

    /// <summary>Who the selected device communicated with in the selected period, or null when not loaded.</summary>
    public DeviceTrafficViewModel? SelectedDeviceTraffic
    {
        get => _selectedDeviceTraffic;
        private set => SetProperty(ref _selectedDeviceTraffic, value);
    }

    /// <summary>Empties the traffic table; from now on it shows only traffic that arrives after this moment.</summary>
    public AsyncRelayCommand ResetTrafficCommand { get; }

    /// <summary>Shows the traffic of the whole selected period again.</summary>
    public AsyncRelayCommand ShowAllTrafficCommand { get; }

    /// <summary>True while the table shows only traffic since it was reset.</summary>
    public bool IsTrafficReset => _trafficSince is not null;

    public string TrafficSinceText => _trafficSince is { } since
        ? $"Zobrazuje sa len komunikácia od {since.ToLocalTime():HH:mm:ss}. Nová pribudne s najbližším hlásením agenta."
        : string.Empty;

    private async Task ResetTrafficAsync()
    {
        var now = _timeProvider.GetUtcNow();

        SetTrafficSince(now);
        await LoadSelectedDeviceTrafficAsync(now);
    }

    private async Task ShowAllTrafficAsync()
    {
        SetTrafficSince(null);
        await LoadSelectedDeviceTrafficAsync(_timeProvider.GetUtcNow());
    }

    private void SetTrafficSince(DateTimeOffset? since)
    {
        _trafficSince = since;
        OnPropertyChanged(nameof(IsTrafficReset));
        OnPropertyChanged(nameof(TrafficSinceText));
        ShowAllTrafficCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Switches capture of network traffic of the selected device on or off.</summary>
    public AsyncRelayCommand ToggleTrafficCaptureCommand { get; }

    public string ToggleTrafficCaptureText => SelectedDevice is { CaptureTraffic: true } ? "Vypnúť zachytávanie" : "Zapnúť zachytávanie";

    /// <summary>Whether the traffic of the selected device is being captured and, if it cannot be, why.</summary>
    public string TrafficCaptureStatusText => SelectedDevice switch
    {
        null => string.Empty,
        { HasAgent: false } => "Komunikáciu možno zachytávať len na zariadení s agentom.",
        { CaptureTraffic: false } => "Zachytávanie komunikácie je vypnuté.",
        { TrafficCaptureProblem.Length: > 0 } device => $"Agent nemôže zachytávať: {device.TrafficCaptureProblem}",
        _ => "Zachytávanie komunikácie je zapnuté. Súhrn prichádza s každým hlásením agenta.",
    };

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

        await LoadSelectedDeviceCommandsAsync();
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

    /// <summary>Optional TCP port for a device that blocks ping, as typed by the user; empty for none.</summary>
    public string NewDeviceTcpPort
    {
        get => _newDeviceTcpPort;
        set
        {
            if (SetProperty(ref _newDeviceTcpPort, value))
            {
                AddDeviceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>An empty text means no port; otherwise it must be a port number from 1 to 65535.</summary>
    private static bool TryParseTcpPort(string text, out int? port)
    {
        port = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 65535)
        {
            port = parsed;

            return true;
        }

        return false;
    }

    /// <summary>The global synchronization interval in seconds, as typed by the user.</summary>
    public string SyncIntervalText
    {
        get => _syncIntervalText;
        set
        {
            if (SetProperty(ref _syncIntervalText, value))
            {
                SaveSettingsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>How many synchronizations in a row a device may miss before it is offline, as typed by the user.</summary>
    public string OfflineThresholdText
    {
        get => _offlineThresholdText;
        set
        {
            if (SetProperty(ref _offlineThresholdText, value))
            {
                SaveSettingsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>How many days history is kept, as typed by the user.</summary>
    public string RetentionDaysText
    {
        get => _retentionDaysText;
        set
        {
            if (SetProperty(ref _retentionDaysText, value))
            {
                SaveSettingsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The allowed ranges of the settings and what they mean together.</summary>
    public string SettingsHint
    {
        get => _settingsHint;
        private set => SetProperty(ref _settingsHint, value);
    }

    /// <summary>Loads the current device list from the server. Called at start-up, periodically and on demand.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var devices = await _server.GetDevicesAsync();

            // The list is rebuilt, so the selection is carried over to the row of the same device.
            var selectedId = SelectedDevice?.Id;
            var before = new Dictionary<string, DeviceRowViewModel>();

            foreach (var device in _allDevices)
            {
                before.TryAdd(device.Id, device);
            }

            _allDevices = devices.Select(device => new DeviceRowViewModel(device)).ToList();
            ShowFilteredDevices(selectedId);

            // The interval is loaded only once, so a periodic refresh does not overwrite what the user is typing.
            if (!_settingsLoaded)
            {
                ShowSettings(await _server.GetSettingsAsync());
            }

            OnPropertyChanged(nameof(DevicesSummaryText));
            OnPropertyChanged(nameof(CanManageEnrollmentCodes));
            UpdateAlerts(before);
            await UpdateOverviewAsync();

            // A periodic refresh does not overwrite what the last action said, only a lost connection it outlived.
            if (LastRefreshedAt is null || IsServerUnavailable)
            {
                StatusMessage = "Pripojené k serveru.";
            }

            LastRefreshedAt = _timeProvider.GetLocalNow();
            IsServerUnavailable = false;
        }
        catch (ServerClientException exception) when (exception.Kind == ServerErrorKind.NotSignedIn)
        {
            EndSession("Prihlásenie vypršalo alebo bolo zrušené. Prihláste sa znova.");
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);
            IsServerUnavailable = exception.Kind == ServerErrorKind.Unavailable;
        }
    }

    public AsyncRelayCommand ExportTrafficCommand { get; }

    public AsyncRelayCommand ExportEventsCommand { get; }

    public AsyncRelayCommand ExportOutagesCommand { get; }

    /// <summary>Exports the traffic as it is shown, filter included, with exact byte counts.</summary>
    private Task ExportTrafficAsync()
    {
        Export(
            "komunikacia",
            ["Vzdialená adresa", "Port", "Protokol", "Transport", "Odoslané (B)", "Prijaté (B)"],
            SelectedDeviceTraffic?.Rows.Select(row => (IReadOnlyList<string>)
                [row.RemoteAddress, row.PortText, row.Protocol, row.Transport, Invariant(row.BytesSent), Invariant(row.BytesReceived)]));

        return Task.CompletedTask;
    }

    private Task ExportEventsAsync()
    {
        Export(
            "udalosti",
            ["Čas", "Udalosť", "Závažnosť", "Správa"],
            SelectedDeviceHistory?.Events.Select(row => (IReadOnlyList<string>)[row.OccurredText, row.TypeText, row.SeverityText, row.Message]));

        return Task.CompletedTask;
    }

    private Task ExportOutagesAsync()
    {
        Export(
            "vypadky",
            ["Začiatok", "Koniec", "Trvanie"],
            SelectedDeviceHistory?.Outages.Select(row => (IReadOnlyList<string>)[row.StartedText, row.EndedText, row.DurationText]));

        return Task.CompletedTask;
    }

    /// <summary>Saves the rows of the selected device's table under a name with the device and the date.</summary>
    private void Export(string table, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>>? rows)
    {
        if (_csvExporter is null || SelectedDevice is not { } device || rows is null)
        {
            StatusMessage = "Nie je čo exportovať.";

            return;
        }

        var name = $"{table}-{SafeFileName(device.Name)}-{_timeProvider.GetLocalNow():yyyy-MM-dd}";

        try
        {
            if (_csvExporter.Export(name, header, rows.ToList()) is { } path)
            {
                StatusMessage = $"Exportované do {path}.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Súbor sa nepodarilo uložiť: {exception.Message}";
        }
    }

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    /// <summary>The whole network at a glance; shown while no device is selected.</summary>
    public NetworkOverviewViewModel? NetworkOverview
    {
        get => _networkOverview;
        private set => SetProperty(ref _networkOverview, value);
    }

    public bool HasSelectedDevice => SelectedDevice is not null;

    /// <summary>
    /// Rebuilds the overview while it is on screen. The warnings come from the event log, which only some users
    /// may read; they are reloaded with the list, and kept as they were when the log cannot be read just now.
    /// </summary>
    private async Task UpdateOverviewAsync()
    {
        if (SelectedDevice is not null)
        {
            return;
        }

        if (CanViewEventLog)
        {
            var now = _timeProvider.GetUtcNow();

            try
            {
                _overviewWarnings = await _server.GetEventLogAsync(now - NetworkOverviewViewModel.WarningsPeriod, now);
            }
            catch (ServerClientException exception) when (exception.Kind != ServerErrorKind.NotSignedIn)
            {
                // The warnings are an extra; the overview still shows the devices.
            }
        }

        ShowOverview();
    }

    private void ShowOverview()
    {
        NetworkOverview = new NetworkOverviewViewModel(_allDevices, CanViewEventLog ? _overviewWarnings ?? new EventLogReply() : null, _timeProvider.GetUtcNow());
    }

    /// <summary>Raised when devices that were online have gone offline, so the window can draw attention to it.</summary>
    public event EventHandler<IReadOnlyList<string>>? DevicesWentOffline;

    /// <summary>The devices that went offline while the application was watching, until the user dismisses them.</summary>
    public string AlertText => _alerts.Count == 0
        ? string.Empty
        : (_alerts.Count == 1 ? "Zariadenie je nedostupné: " : "Zariadenia sú nedostupné: ")
            + string.Join(", ", _alerts.Values.OrderBy(alert => alert.Since).Select(alert => $"{alert.Name} (od {alert.Since:HH:mm:ss})"));

    public bool HasAlert => _alerts.Count > 0;

    public AsyncRelayCommand DismissAlertsCommand { get; }

    /// <summary>
    /// Notes enabled devices that went from online to offline since the previous refresh. A device that comes back
    /// online, is disabled or removed drops out of the alert by itself. The first load only learns the states.
    /// </summary>
    private void UpdateAlerts(Dictionary<string, DeviceRowViewModel> before)
    {
        var now = _timeProvider.GetLocalNow();
        var wentOffline = new List<string>();

        foreach (var device in _allDevices)
        {
            if (device.IsEnabled && device.Status == DeviceStatus.Offline
                && before.TryGetValue(device.Id, out var earlier) && earlier.IsEnabled && earlier.Status == DeviceStatus.Online)
            {
                _alerts[device.Id] = (device.Name, now);
                wentOffline.Add(device.Name);
            }
        }

        foreach (var id in _alerts.Keys.ToList())
        {
            if (_allDevices.FirstOrDefault(device => device.Id == id) is not { IsEnabled: true, Status: DeviceStatus.Offline })
            {
                _alerts.Remove(id);
            }
        }

        OnPropertyChanged(nameof(AlertText));
        OnPropertyChanged(nameof(HasAlert));

        if (wentOffline.Count > 0)
        {
            DevicesWentOffline?.Invoke(this, wentOffline);
        }
    }

    private Task DismissAlertsAsync()
    {
        _alerts.Clear();
        OnPropertyChanged(nameof(AlertText));
        OnPropertyChanged(nameof(HasAlert));

        return Task.CompletedTask;
    }

    /// <summary>When the device list was last loaded successfully; null before the first time.</summary>
    public DateTimeOffset? LastRefreshedAt
    {
        get => _lastRefreshedAt;
        private set
        {
            if (SetProperty(ref _lastRefreshedAt, value))
            {
                OnPropertyChanged(nameof(LastRefreshText));
                OnPropertyChanged(nameof(ConnectionWarningText));
            }
        }
    }

    public string LastRefreshText => LastRefreshedAt is { } refreshedAt ? $"Obnovené {refreshedAt:HH:mm:ss}" : string.Empty;

    /// <summary>True while the server cannot be reached; what is on screen then is getting old.</summary>
    public bool IsServerUnavailable
    {
        get => _isServerUnavailable;
        private set
        {
            if (SetProperty(ref _isServerUnavailable, value))
            {
                OnPropertyChanged(nameof(ConnectionWarningText));
            }
        }
    }

    public string ConnectionWarningText => !IsServerUnavailable
        ? string.Empty
        : LastRefreshedAt is { } refreshedAt
            ? $"Server nie je dostupný. Zobrazené údaje sú z {refreshedAt:HH:mm:ss}; aplikácia to skúša znova každých 5 sekúnd."
            : "Server nie je dostupný; aplikácia to skúša znova každých 5 sekúnd.";

    private async Task AddDeviceAsync()
    {
        try
        {
            TryParseTcpPort(NewDeviceTcpPort, out var tcpPort);

            await _server.AddAgentlessDeviceAsync(NewDeviceName.Trim(), NewDeviceIpAddress.Trim(), tcpPort);
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);
            AddDeviceMessage = StatusMessage;

            return;
        }

        var addedName = NewDeviceName.Trim();
        ClearNewDevice();

        await RefreshAsync();

        StatusMessage = $"Zariadenie „{addedName}“ bolo pridané. {StatusMessage}";
        DeviceAdded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when a device has been added, so the form that added it can close.</summary>
    public event EventHandler? DeviceAdded;

    /// <summary>Why the device could not be added; shown in the form, which hides the main window's status bar.</summary>
    public string AddDeviceMessage
    {
        get => _addDeviceMessage;
        private set => SetProperty(ref _addDeviceMessage, value);
    }

    /// <summary>Empties the form for adding a device, so it opens without what was typed before.</summary>
    public void ClearNewDevice()
    {
        NewDeviceName = string.Empty;
        NewDeviceIpAddress = string.Empty;
        NewDeviceTcpPort = string.Empty;
        AddDeviceMessage = string.Empty;
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

    /// <summary>Asks for a new name of the selected device and saves it.</summary>
    public AsyncRelayCommand RenameDeviceCommand { get; }

    /// <summary>The longest name the server accepts.</summary>
    public const int MaxDeviceNameLength = 200;

    private async Task RenameSelectedDeviceAsync()
    {
        var device = SelectedDevice;

        if (device is null
            || _textInput?.AskForText("Premenovať zariadenie", $"_Nový názov zariadenia „{device.Name}“:", device.Name, MaxDeviceNameLength) is not { } name
            || name.Trim().Length == 0
            || name.Trim() == device.Name)
        {
            return;
        }

        try
        {
            await _server.RenameDeviceAsync(device.Id, name.Trim());
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);

            return;
        }

        await RefreshAsync();

        StatusMessage = $"Zariadenie „{device.Name}“ bolo premenované na „{name.Trim()}“. {StatusMessage}";
    }

    private async Task ToggleSelectedDeviceEnabledAsync()
    {
        var device = SelectedDevice;

        if (device is null)
        {
            return;
        }

        var enable = !device.IsEnabled;

        if (!enable && !_confirmation.Confirm(
                "Zakázať zariadenie",
                $"Naozaj chcete zakázať zariadenie „{device.Name}“? Prestane sa sledovať a jeho agent bude odmietnutý, kým ho znova nepovolíte."))
        {
            return;
        }

        try
        {
            await _server.SetDeviceEnabledAsync(device.Id, enable);
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);

            return;
        }

        await RefreshAsync();

        StatusMessage = enable
            ? $"Zariadenie „{device.Name}“ bolo povolené. {StatusMessage}"
            : $"Zariadenie „{device.Name}“ bolo zakázané. {StatusMessage}";
    }

    private async Task ToggleSelectedDeviceTrafficCaptureAsync()
    {
        var device = SelectedDevice;

        if (device is not { HasAgent: true })
        {
            return;
        }

        var capture = !device.CaptureTraffic;

        if (capture && !_confirmation.Confirm(
                "Zapnúť zachytávanie komunikácie",
                $"Agent na zariadení „{device.Name}“ bude zaznamenávať, s kým zariadenie komunikuje a koľko dát prenesie. "
                + "Obsah komunikácie sa neukladá. Na zariadení musí byť nainštalovaný Wireshark s Npcap. Pokračovať?"))
        {
            return;
        }

        try
        {
            await _server.SetDeviceTrafficCaptureAsync(device.Id, capture);
        }
        catch (ServerClientException exception)
        {
            StatusMessage = Describe(exception);

            return;
        }

        await RefreshAsync();

        StatusMessage = capture
            ? $"Zachytávanie komunikácie na zariadení „{device.Name}“ sa začne pri jeho najbližšom hlásení. {StatusMessage}"
            : $"Zachytávanie komunikácie na zariadení „{device.Name}“ bolo vypnuté. {StatusMessage}";
    }

    /// <summary>Sends only the settings that differ from what the server last reported, one after another.</summary>
    private async Task SaveSettingsAsync()
    {
        var seconds = ParseWholeNumber(SyncIntervalText);
        var missedSyncs = ParseWholeNumber(OfflineThresholdText);
        var days = ParseWholeNumber(RetentionDaysText);
        var shown = _shownSettings;
        var changed = false;

        try
        {
            if (shown is null || seconds != shown.SyncIntervalSeconds)
            {
                shown = await _server.UpdateSyncIntervalAsync(seconds);
                changed = true;
            }

            if (missedSyncs != shown.OfflineAfterMissedSyncs)
            {
                shown = await _server.UpdateOfflineThresholdAsync(missedSyncs);
                changed = true;
            }

            if (days != shown.RetentionDays)
            {
                shown = await _server.UpdateRetentionAsync(days);
                changed = true;
            }
        }
        catch (ServerClientException exception)
        {
            // What the user typed stays, so the wrong value can be corrected; what was saved is remembered.
            _shownSettings = shown;
            StatusMessage = Describe(exception);
            SettingsMessage = StatusMessage;

            return;
        }

        ShowSettings(shown);
        StatusMessage = changed
            ? $"Nastavenia sú uložené: interval {seconds} s, offline po {missedSyncs} vynechaných intervaloch, história {days} dní."
            : "Nastavenia sa nezmenili.";
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the settings have been saved, so the form can close.</summary>
    public event EventHandler? SettingsSaved;

    /// <summary>Why the settings could not be saved; shown in the form.</summary>
    public string SettingsMessage
    {
        get => _settingsMessage;
        private set => SetProperty(ref _settingsMessage, value);
    }

    /// <summary>Puts the saved settings back into the form, so it opens without changes that were not saved.</summary>
    public void ResetSettingsForm()
    {
        if (_shownSettings is not null)
        {
            ShowSettings(_shownSettings);
        }

        SettingsMessage = string.Empty;
    }

    private void ShowSettings(MonitoringSettingsInfo settings)
    {
        _settingsLoaded = true;
        _shownSettings = settings;
        SyncIntervalText = settings.SyncIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        OfflineThresholdText = settings.OfflineAfterMissedSyncs.ToString(CultureInfo.InvariantCulture);
        RetentionDaysText = settings.RetentionDays.ToString(CultureInfo.InvariantCulture);

        var offlineAfter = TimeSpan.FromSeconds((long)settings.SyncIntervalSeconds * settings.OfflineAfterMissedSyncs);
        SettingsHint =
            $"Interval {settings.MinSyncIntervalSeconds} – {settings.MaxSyncIntervalSeconds} s, "
            + $"vynechané intervaly 1 – {settings.MaxOfflineAfterMissedSyncs}, história 1 – {settings.MaxRetentionDays} dní. "
            + $"Zariadenie je teraz offline po {DeviceHistoryViewModel.FormatDuration(offlineAfter)} bez kontaktu.";
    }

    private static bool IsWholeNumber(string text)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static int ParseWholeNumber(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string Describe(ServerClientException exception)
    {
        return exception.Kind switch
        {
            ServerErrorKind.Unavailable => "Server nie je dostupný.",
            ServerErrorKind.AccessDenied => "Na túto operáciu nemáte oprávnenie.",
            ServerErrorKind.NotSignedIn => "Prihlásenie vypršalo. Prihláste sa znova.",
            ServerErrorKind.InvalidInput => $"Server odmietol zadané údaje: {exception.Message}",
            _ => $"Chyba servera: {exception.Message}",
        };
    }
}
