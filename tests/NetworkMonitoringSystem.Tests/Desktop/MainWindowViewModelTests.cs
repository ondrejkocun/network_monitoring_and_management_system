namespace NetworkMonitoringSystem.Tests.Desktop;

using Google.Protobuf.WellKnownTypes;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public class MainWindowViewModelTests
{
    private readonly FakeServerClient _server = new();
    private readonly FakeConfirmationDialog _confirmation = new();

    [Fact]
    public void StatusMessage_RaisesPropertyChanged_OnlyWhenValueChanges()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.StatusMessage = "Pripojene";
        viewModel.StatusMessage = "Pripojene";

        Assert.Equal([nameof(MainWindowViewModel.StatusMessage)], changedProperties);
    }

    [Fact]
    public async Task RefreshAsync_ShowsDevicesFromServer_AndSummary()
    {
        var lastSeen = new DateTimeOffset(2026, 10, 9, 11, 30, 0, TimeSpan.Zero);
        _server.Devices.Add(new DeviceInfo
        {
            Id = Guid.NewGuid().ToString(),
            Name = "PC-01",
            MonitoringMode = MonitoringMode.Agent,
            HostName = "pc-01",
            Status = DeviceStatus.Online,
            LastSeenAt = Timestamp.FromDateTimeOffset(lastSeen),
            OperatingSystem = "Windows 11",
        });
        _server.Devices.Add(new DeviceInfo
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Router",
            MonitoringMode = MonitoringMode.Agentless,
            IpAddress = "192.168.50.1",
            Status = DeviceStatus.Offline,
        });
        var viewModel = new MainWindowViewModel(_server, _confirmation);

        await viewModel.RefreshAsync();

        Assert.Equal(2, viewModel.Devices.Count);

        var computer = viewModel.Devices[0];
        Assert.Equal("PC-01", computer.Name);
        Assert.Equal("pc-01", computer.Address);
        Assert.Equal("S agentom", computer.ModeText);
        Assert.Equal("Online", computer.StatusText);
        Assert.Equal(lastSeen, computer.LastSeenAt);
        Assert.Equal(lastSeen.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), computer.LastSeenText);

        var router = viewModel.Devices[1];
        Assert.Equal("192.168.50.1", router.Address);
        Assert.Equal("Bez agenta", router.ModeText);
        Assert.Equal("Offline", router.StatusText);
        Assert.Equal("–", router.LastSeenText);

        Assert.Contains("Zariadenia: 2, z toho online: 1", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshAsync_ReplacesPreviousList()
    {
        _server.Devices.Add(new DeviceInfo { Name = "Old" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        _server.Devices.Clear();
        _server.Devices.Add(new DeviceInfo { Name = "New" });

        await viewModel.RefreshAsync();

        Assert.Equal("New", Assert.Single(viewModel.Devices).Name);
    }

    [Fact]
    public async Task RefreshAsync_WhenServerIsUnavailable_ShowsMessage_AndKeepsList()
    {
        _server.Devices.Add(new DeviceInfo { Name = "PC-01" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        _server.Failure = new ServerClientException(ServerErrorKind.Unavailable, "connection refused");

        await viewModel.RefreshAsync();

        Assert.Equal("Server nie je dostupný.", viewModel.StatusMessage);
        Assert.Single(viewModel.Devices);
    }

    [Fact]
    public async Task RefreshAsync_LoadsIntervalOnce_SoItDoesNotOverwriteWhatUserTypes()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        Assert.Equal("60", viewModel.SyncIntervalText);
        Assert.Contains("5 – 86400", viewModel.SyncIntervalHint);

        viewModel.SyncIntervalText = "12";
        await viewModel.RefreshAsync();

        Assert.Equal("12", viewModel.SyncIntervalText);
    }

    [Fact]
    public void AddDeviceCommand_IsDisabled_UntilNameAndAddressAreFilled()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        Assert.False(viewModel.AddDeviceCommand.CanExecute(null));

        viewModel.NewDeviceName = "Router";
        Assert.False(viewModel.AddDeviceCommand.CanExecute(null));

        viewModel.NewDeviceIpAddress = "192.168.50.1";
        Assert.True(viewModel.AddDeviceCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddDeviceCommand_SendsTrimmedValues_ClearsForm_AndRefreshesList()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation)
        {
            NewDeviceName = "  Router ",
            NewDeviceIpAddress = " 192.168.50.1 ",
        };

        await viewModel.AddDeviceCommand.ExecuteAsync();

        Assert.Equal(("Router", "192.168.50.1"), Assert.Single(_server.AddedDevices));
        Assert.Equal(string.Empty, viewModel.NewDeviceName);
        Assert.Equal(string.Empty, viewModel.NewDeviceIpAddress);
        Assert.Equal("Router", Assert.Single(viewModel.Devices).Name);
        Assert.Contains("bolo pridané", viewModel.StatusMessage);
    }

    [Fact]
    public async Task AddDeviceCommand_WhenServerRejectsInput_ShowsReason_AndKeepsForm()
    {
        _server.Failure = new ServerClientException(ServerErrorKind.InvalidInput, "'abc' is not a valid IP address.");
        var viewModel = new MainWindowViewModel(_server, _confirmation)
        {
            NewDeviceName = "Router",
            NewDeviceIpAddress = "abc",
        };

        await viewModel.AddDeviceCommand.ExecuteAsync();

        Assert.Contains("'abc' is not a valid IP address.", viewModel.StatusMessage);
        Assert.Equal("Router", viewModel.NewDeviceName);
        Assert.Equal("abc", viewModel.NewDeviceIpAddress);
    }

    [Fact]
    public async Task RemoveDeviceCommand_IsDisabled_UntilDeviceIsSelected()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        Assert.False(viewModel.RemoveDeviceCommand.CanExecute(null));

        viewModel.SelectedDevice = viewModel.Devices[0];

        Assert.True(viewModel.RemoveDeviceCommand.CanExecute(null));
    }

    [Fact]
    public async Task RemoveDeviceCommand_AsksForConfirmation_ThenRemovesDevice_AndRefreshesList()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        _server.Devices.Add(new DeviceInfo { Id = "device-2", Name = "Switch" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];

        await viewModel.RemoveDeviceCommand.ExecuteAsync();

        Assert.Contains("Router", Assert.Single(_confirmation.Questions));
        Assert.Equal(["device-1"], _server.RemovedDeviceIds);
        Assert.Equal("Switch", Assert.Single(viewModel.Devices).Name);
        Assert.Null(viewModel.SelectedDevice);
        Assert.Contains("bolo odstránené", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RemoveDeviceCommand_DoesNothing_WhenUserDeclines()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        _confirmation.Answer = false;
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];

        await viewModel.RemoveDeviceCommand.ExecuteAsync();

        Assert.Empty(_server.RemovedDeviceIds);
        Assert.Single(viewModel.Devices);
        Assert.NotNull(viewModel.SelectedDevice);
    }

    [Fact]
    public async Task RemoveDeviceCommand_WhenServerIsUnavailable_ShowsMessage_AndKeepsDevice()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        _server.Failure = new ServerClientException(ServerErrorKind.Unavailable, "connection refused");

        await viewModel.RemoveDeviceCommand.ExecuteAsync();

        Assert.Equal("Server nie je dostupný.", viewModel.StatusMessage);
        Assert.Single(viewModel.Devices);
    }

    [Fact]
    public async Task RefreshAsync_KeepsSelection_OnTheSameDevice()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        _server.Devices.Add(new DeviceInfo { Id = "device-2", Name = "Switch" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[1];

        await viewModel.RefreshAsync();

        Assert.Same(viewModel.Devices[1], viewModel.SelectedDevice);
        Assert.Equal("device-2", viewModel.SelectedDevice!.Id);
    }

    [Fact]
    public async Task RefreshAsync_ClearsSelection_WhenSelectedDeviceNoLongerExists()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        _server.Devices.Clear();

        await viewModel.RefreshAsync();

        Assert.Null(viewModel.SelectedDevice);
        Assert.False(viewModel.RemoveDeviceCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public void SaveSyncIntervalCommand_IsDisabled_ForTextThatIsNotAWholeNumber(string text)
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation) { SyncIntervalText = text };

        Assert.False(viewModel.SaveSyncIntervalCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveSyncIntervalCommand_SendsNewInterval_AndConfirms()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation) { SyncIntervalText = "30" };

        await viewModel.SaveSyncIntervalCommand.ExecuteAsync();

        Assert.Equal(30, _server.SyncIntervalSeconds);
        Assert.Equal("30", viewModel.SyncIntervalText);
        Assert.Contains("30 s", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveSyncIntervalCommand_WhenServerRejectsInterval_ShowsReason()
    {
        _server.Failure = new ServerClientException(ServerErrorKind.InvalidInput, "Interval must be between 5 and 86400 seconds.");
        var viewModel = new MainWindowViewModel(_server, _confirmation) { SyncIntervalText = "1" };

        await viewModel.SaveSyncIntervalCommand.ExecuteAsync();

        Assert.Contains("between 5 and 86400", viewModel.StatusMessage);
        Assert.Equal(60, _server.SyncIntervalSeconds);
    }

    private sealed class FakeServerClient : IServerClient
    {
        public List<DeviceInfo> Devices { get; } = [];

        public List<(string Name, string IpAddress)> AddedDevices { get; } = [];

        public List<string> RemovedDeviceIds { get; } = [];

        public int SyncIntervalSeconds { get; private set; } = 60;

        /// <summary>When set, every call fails with this exception.</summary>
        public ServerClientException? Failure { get; set; }

        public Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            return Task.FromResult<IReadOnlyList<DeviceInfo>>(Devices.ToList());
        }

        public Task AddAgentlessDeviceAsync(string name, string ipAddress, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            AddedDevices.Add((name, ipAddress));
            Devices.Add(new DeviceInfo { Name = name, IpAddress = ipAddress, MonitoringMode = MonitoringMode.Agentless });

            return Task.CompletedTask;
        }

        public Task RemoveDeviceAsync(string id, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            RemovedDeviceIds.Add(id);
            Devices.RemoveAll(device => device.Id == id);

            return Task.CompletedTask;
        }

        public Task<MonitoringSettingsInfo> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            return Task.FromResult(CreateSettings());
        }

        public Task<MonitoringSettingsInfo> UpdateSyncIntervalAsync(int seconds, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            SyncIntervalSeconds = seconds;

            return Task.FromResult(CreateSettings());
        }

        private MonitoringSettingsInfo CreateSettings()
        {
            return new MonitoringSettingsInfo
            {
                SyncIntervalSeconds = SyncIntervalSeconds,
                OfflineAfterMissedSyncs = 3,
                MinSyncIntervalSeconds = 5,
                MaxSyncIntervalSeconds = 86400,
            };
        }

        private void ThrowIfFailing()
        {
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }

    private sealed class FakeConfirmationDialog : IConfirmationDialog
    {
        public bool Answer { get; set; } = true;

        public List<string> Questions { get; } = [];

        public bool Confirm(string title, string question)
        {
            Questions.Add(question);

            return Answer;
        }
    }
}