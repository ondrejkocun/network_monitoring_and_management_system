namespace NetworkMonitoringSystem.Tests.Desktop;

using Google.Protobuf.WellKnownTypes;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;
using NetworkMonitoringSystem.Tests.Fakes;

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
            IsEnabled = true,
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
            IsEnabled = true,
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
        Assert.Equal(("60", "3", "30"), (viewModel.SyncIntervalText, viewModel.OfflineThresholdText, viewModel.RetentionDaysText));
        Assert.Contains("5 – 86400", viewModel.SettingsHint);
        Assert.Contains("offline po 3 min 0 s", viewModel.SettingsHint);

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

        Assert.Equal(("Router", "192.168.50.1", (int?)null), Assert.Single(_server.AddedDevices));
        Assert.Equal(string.Empty, viewModel.NewDeviceName);
        Assert.Equal(string.Empty, viewModel.NewDeviceIpAddress);
        Assert.Equal("Router", Assert.Single(viewModel.Devices).Name);
        Assert.Contains("bolo pridané", viewModel.StatusMessage);
    }

    [Fact]
    public async Task AddDeviceCommand_SendsTcpPort_AndClearsIt()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation)
        {
            NewDeviceName = "NAS",
            NewDeviceIpAddress = "192.168.50.5",
            NewDeviceTcpPort = " 445 ",
        };

        await viewModel.AddDeviceCommand.ExecuteAsync();

        Assert.Equal(445, Assert.Single(_server.AddedDevices).TcpPort);
        Assert.Equal(string.Empty, viewModel.NewDeviceTcpPort);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("http")]
    [InlineData("-1")]
    public void AddDeviceCommand_IsDisabled_ForInvalidTcpPort(string tcpPort)
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation)
        {
            NewDeviceName = "NAS",
            NewDeviceIpAddress = "192.168.50.5",
            NewDeviceTcpPort = tcpPort,
        };

        Assert.False(viewModel.AddDeviceCommand.CanExecute(null));
    }

    [Fact]
    public void DeviceRow_ShowsTcpPortNextToAddress()
    {
        var row = new DeviceRowViewModel(new DeviceInfo { IpAddress = "192.168.50.5", TcpPort = 445 });

        Assert.Equal("192.168.50.5 (TCP 445)", row.Address);
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
    public async Task ToggleDeviceEnabledCommand_AfterConfirmation_DisablesDevice_AndOffersToEnableIt()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router", IsEnabled = true, Status = DeviceStatus.Online });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        Assert.False(viewModel.ToggleDeviceEnabledCommand.CanExecute(null));
        viewModel.SelectedDevice = viewModel.Devices[0];
        Assert.Equal("Zakázať vybrané", viewModel.ToggleDeviceEnabledText);

        await viewModel.ToggleDeviceEnabledCommand.ExecuteAsync();

        Assert.Contains("Router", Assert.Single(_confirmation.Questions));
        var row = Assert.Single(viewModel.Devices);
        Assert.False(row.IsEnabled);
        Assert.Equal("Zakázané", row.StatusText);
        Assert.Same(row, viewModel.SelectedDevice);
        Assert.Equal("Povoliť vybrané", viewModel.ToggleDeviceEnabledText);
        Assert.Contains("bolo zakázané", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ToggleDeviceEnabledCommand_EnablesDisabledDevice_WithoutAsking()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router", IsEnabled = false });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];

        await viewModel.ToggleDeviceEnabledCommand.ExecuteAsync();

        Assert.Empty(_confirmation.Questions);
        Assert.True(Assert.Single(viewModel.Devices).IsEnabled);
        Assert.Contains("bolo povolené", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ToggleDeviceEnabledCommand_DoesNothing_WhenUserDeclines()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router", IsEnabled = true });
        _confirmation.Answer = false;
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];

        await viewModel.ToggleDeviceEnabledCommand.ExecuteAsync();

        Assert.True(_server.Devices[0].IsEnabled);
    }

    [Fact]
    public async Task ToggleDeviceEnabledCommand_WhenRefused_ShowsMessage_AndKeepsDeviceEnabled()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router", IsEnabled = true });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        _server.Failure = new ServerClientException(ServerErrorKind.Unavailable, "connection refused");

        await viewModel.ToggleDeviceEnabledCommand.ExecuteAsync();

        Assert.Equal("Server nie je dostupný.", viewModel.StatusMessage);
        Assert.True(viewModel.Devices[0].IsEnabled);
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

    [Fact]
    public async Task SelectingDevice_ShowsItsLatestResources()
    {
        const ulong gigabyte = 1024UL * 1024 * 1024;
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.Resources["device-1"] = new DeviceResourcesReply
        {
            HasData = true,
            RecordedAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 10, 9, 11, 30, 0, TimeSpan.Zero)),
            CpuUsagePercent = 12.34,
            MemoryTotalBytes = 16 * gigabyte,
            MemoryUsedBytes = 4 * gigabyte,
            Disks = { new DiskInfo { Name = "C:\\", TotalBytes = 200 * gigabyte, FreeBytes = 50 * gigabyte } },
            NetworkInterfaces =
            {
                new NetworkInterfaceInfo
                {
                    Name = "Ethernet", IpAddress = "192.168.50.20", MacAddress = "AA:BB:CC:DD:EE:FF",
                    IsUp = true, BytesSent = 1536, BytesReceived = 3 * gigabyte,
                },
            },
        };
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        Assert.Null(viewModel.SelectedDeviceResources);
        Assert.Equal("Vyberte zariadenie v zozname.", viewModel.ResourcesMessage);

        viewModel.SelectedDevice = viewModel.Devices[0];

        var resources = viewModel.SelectedDeviceResources;
        Assert.NotNull(resources);
        Assert.Equal(string.Empty, viewModel.ResourcesMessage);
        Assert.Equal("12,3 %", resources.CpuText);
        Assert.Equal("4 GB z 16 GB (25 %)", resources.MemoryText);

        var disk = Assert.Single(resources.Disks);
        Assert.Equal("C:\\", disk.Name);
        Assert.Equal("150 GB z 200 GB (75 %)", disk.UsageText);
        Assert.Equal("50 GB", disk.FreeText);

        var networkInterface = Assert.Single(resources.NetworkInterfaces);
        Assert.Equal("Ethernet", networkInterface.Name);
        Assert.Equal("Aktívne", networkInterface.StateText);
        Assert.Equal("1,5 kB", networkInterface.SentText);
        Assert.Equal("3 GB", networkInterface.ReceivedText);
    }

    [Fact]
    public async Task SelectingDeviceWithoutResources_ExplainsWhyNothingIsShown()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "Router" });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();

        viewModel.SelectedDevice = viewModel.Devices[0];

        Assert.Null(viewModel.SelectedDeviceResources);
        Assert.Contains("neposlalo žiadne údaje", viewModel.ResourcesMessage);
    }

    [Fact]
    public async Task SelectingAnotherDevice_ReplacesResources_AndDeselectingClearsThem()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.Devices.Add(new DeviceInfo { Id = "device-2", Name = "PC-02" });
        _server.Resources["device-1"] = new DeviceResourcesReply { HasData = true, CpuUsagePercent = 10 };
        _server.Resources["device-2"] = new DeviceResourcesReply { HasData = true, CpuUsagePercent = 90 };
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();

        viewModel.SelectedDevice = viewModel.Devices[0];
        Assert.Equal("10 %", viewModel.SelectedDeviceResources!.CpuText);

        viewModel.SelectedDevice = viewModel.Devices[1];
        Assert.Equal("90 %", viewModel.SelectedDeviceResources!.CpuText);

        viewModel.SelectedDevice = null;
        Assert.Null(viewModel.SelectedDeviceResources);
        Assert.Equal("Vyberte zariadenie v zozname.", viewModel.ResourcesMessage);
    }

    [Fact]
    public async Task RefreshAsync_ReloadsResourcesOfSelectedDevice()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.Resources["device-1"] = new DeviceResourcesReply { HasData = true, CpuUsagePercent = 10 };
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        _server.Resources["device-1"] = new DeviceResourcesReply { HasData = true, CpuUsagePercent = 55 };

        await viewModel.RefreshAsync();

        Assert.Equal("55 %", viewModel.SelectedDeviceResources!.CpuText);
    }

    [Fact]
    public async Task SelectingDevice_ShowsItsProcessesPortsAndConnections()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.Activity["device-1"] = new DeviceActivityReply
        {
            Processes =
            {
                new ProcessDetail { Pid = 100, Name = "nginx", HasUsage = true, CpuUsagePercent = 12.5, MemoryBytes = 2048 },
                new ProcessDetail { Pid = 200, Name = "notepad", MemoryBytes = 4096 },
            },
            ListeningPorts =
            {
                new ListeningPortDetail { Protocol = "TCP", LocalAddress = "0.0.0.0", Port = 80, Pid = 100, ProcessName = "nginx" },
                new ListeningPortDetail { Protocol = "UDP", LocalAddress = "0.0.0.0", Port = 53 },
            },
            Connections =
            {
                new ConnectionDetail
                {
                    Protocol = "TCP", LocalAddress = "192.168.50.20", LocalPort = 50123,
                    RemoteAddress = "2606:2800:220:1::1", RemotePort = 443, State = "ESTABLISHED", Pid = 100, ProcessName = "nginx",
                },
            },
        };
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        Assert.Null(viewModel.SelectedDeviceActivity);

        viewModel.SelectedDevice = viewModel.Devices[0];

        var activity = viewModel.SelectedDeviceActivity;
        Assert.NotNull(activity);
        Assert.Equal("Procesy (2)", activity.ProcessesHeader);
        Assert.Equal("Otvorené porty (2)", activity.ListeningPortsHeader);
        Assert.Equal("Spojenia (1)", activity.ConnectionsHeader);

        Assert.Equal(("nginx", 100U, "12,5 %", "2 kB"), (activity.Processes[0].Name, activity.Processes[0].Pid, activity.Processes[0].CpuText, activity.Processes[0].MemoryText));
        // Usage is shown only where the server recorded it.
        Assert.Equal((string.Empty, string.Empty), (activity.Processes[1].CpuText, activity.Processes[1].MemoryText));

        Assert.Equal("nginx (100)", activity.ListeningPorts[0].ProcessText);
        Assert.Equal("–", activity.ListeningPorts[1].ProcessText);

        var connection = Assert.Single(activity.Connections);
        Assert.Equal("192.168.50.20:50123", connection.LocalEndpoint);
        Assert.Equal("[2606:2800:220:1::1]:443", connection.RemoteEndpoint);
    }

    [Fact]
    public async Task RefreshAsync_KeepsActivityViewModel_WhenNothingChanged_AndReplacesItWhenItDid()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.Activity["device-1"] = new DeviceActivityReply { Processes = { new ProcessDetail { Pid = 100, Name = "nginx" } } };
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        var shown = viewModel.SelectedDeviceActivity;

        // The same data arrive in a new message; the list on screen must not be rebuilt.
        _server.Activity["device-1"] = new DeviceActivityReply { Processes = { new ProcessDetail { Pid = 100, Name = "nginx" } } };
        await viewModel.RefreshAsync();
        Assert.Same(shown, viewModel.SelectedDeviceActivity);

        _server.Activity["device-1"] = new DeviceActivityReply { Processes = { new ProcessDetail { Pid = 300, Name = "calc" } } };
        await viewModel.RefreshAsync();
        Assert.NotSame(shown, viewModel.SelectedDeviceActivity);
        Assert.Equal("calc", viewModel.SelectedDeviceActivity!.Processes[0].Name);
    }

    [Fact]
    public async Task ResourcesWithoutCpuValue_ShowDash()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.Resources["device-1"] = new DeviceResourcesReply { HasData = true };
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();

        viewModel.SelectedDevice = viewModel.Devices[0];

        Assert.Equal("–", viewModel.SelectedDeviceResources!.CpuText);
        Assert.Equal("–", viewModel.SelectedDeviceResources.MemoryText);
    }

    [Fact]
    public async Task SelectingDevice_ShowsItsHistory_ForTheLast24HoursByDefault()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.History["device-1"] = new DeviceHistoryReply
        {
            AvailabilityPercent = 99.4567,
            DowntimeSeconds = 7500,
            Outages =
            {
                new OutageInfo { StartedAt = Timestamp.FromDateTimeOffset(now.AddMinutes(-10)) },
                new OutageInfo
                {
                    StartedAt = Timestamp.FromDateTimeOffset(now.AddHours(-5)),
                    EndedAt = Timestamp.FromDateTimeOffset(now.AddHours(-5).AddMinutes(2).AddSeconds(5)),
                },
            },
            Events =
            {
                new EventInfo
                {
                    OccurredAt = Timestamp.FromDateTimeOffset(now.AddMinutes(-10)),
                    Type = "DeviceWentOffline",
                    Severity = EventSeverity.Warning,
                    Message = "Device 'PC-01' went offline.",
                },
            },
            EventsTruncated = true,
        };
        var viewModel = new MainWindowViewModel(_server, _confirmation, clock);
        await viewModel.RefreshAsync();
        Assert.Null(viewModel.SelectedDeviceHistory);

        viewModel.SelectedDevice = viewModel.Devices[0];

        Assert.Equal(("device-1", now.AddHours(-24), now), Assert.Single(_server.HistoryRequests));

        var history = viewModel.SelectedDeviceHistory;
        Assert.NotNull(history);
        Assert.Equal("99,46 %", history.AvailabilityText);
        Assert.Equal("2 h 5 min", history.DowntimeText);
        Assert.Equal("Výpadky (2)", history.OutagesHeader);
        Assert.Equal("Udalosti (1+)", history.EventsHeader);

        // An outage in progress has no end and is measured to now.
        Assert.Equal(("trvá", "10 min 0 s"), (history.Outages[0].EndedText, history.Outages[0].DurationText));
        Assert.Equal(now.AddHours(-5).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), history.Outages[1].StartedText);
        Assert.Equal("2 min 5 s", history.Outages[1].DurationText);

        var shownEvent = Assert.Single(history.Events);
        Assert.Equal(("Zariadenie offline", "Varovanie", "Device 'PC-01' went offline."), (shownEvent.TypeText, shownEvent.SeverityText, shownEvent.Message));
    }

    [Fact]
    public async Task SelectingDevice_ShowsItsTraffic_LargestFirst_WithTotals()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01", MonitoringMode = MonitoringMode.Agent, IsEnabled = true, CaptureTraffic = true });
        _server.Traffic["device-1"] =
        [
            new TrafficEntry { Transport = "Tcp", Protocol = "TLSv1.3", RemoteAddress = "160.79.104.10", ServicePort = 443, PacketsSent = 10, PacketsReceived = 20, BytesSent = 2048, BytesReceived = 1048576 },
            new TrafficEntry { Transport = "Icmp", Protocol = "ICMP", RemoteAddress = "1.1.1.1", PacketsSent = 1, PacketsReceived = 1, BytesSent = 74, BytesReceived = 74 },
        ];
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();

        viewModel.SelectedDevice = viewModel.Devices[0];

        var traffic = viewModel.SelectedDeviceTraffic;
        Assert.NotNull(traffic);
        Assert.Equal("Komunikácia (2)", traffic.Header);
        Assert.Equal(("160.79.104.10", "443", "TCP", "TLSv1.3"), (traffic.Rows[0].RemoteAddress, traffic.Rows[0].PortText, traffic.Rows[0].Transport, traffic.Rows[0].Protocol));
        Assert.Equal("–", traffic.Rows[1].PortText);
        Assert.Contains("1 MB (20 p.)", traffic.Rows[0].ReceivedText);
        Assert.Contains("2 konverzáciách", traffic.TotalText);
        Assert.Contains("zapnuté", viewModel.TrafficCaptureStatusText);
    }

    [Fact]
    public void TrafficFilter_NarrowsRowsByTextPortOrTransport_AndStaysWhenNewDataArrives()
    {
        var traffic = new DeviceTrafficViewModel(
        [
            new TrafficEntry { Transport = "Tcp", Protocol = "TLSv1.3", RemoteAddress = "160.79.104.10", ServicePort = 443, BytesSent = 100, BytesReceived = 900 },
            new TrafficEntry { Transport = "Udp", Protocol = "DNS", RemoteAddress = "192.168.50.1", ServicePort = 53, BytesSent = 10, BytesReceived = 20 },
            new TrafficEntry { Transport = "Icmp", Protocol = "ICMP", RemoteAddress = "1.1.1.1", BytesSent = 74, BytesReceived = 74 },
        ]);

        traffic.FilterText = "tls";
        Assert.Equal(["160.79.104.10"], traffic.Rows.Select(row => row.RemoteAddress));
        Assert.Equal("Komunikácia (1 z 3)", traffic.Header);

        traffic.FilterText = "53";
        Assert.Equal(["DNS"], traffic.Rows.Select(row => row.Protocol));

        traffic.FilterText = string.Empty;
        traffic.SelectedTransport = "ICMP";
        Assert.Equal(["1.1.1.1"], traffic.Rows.Select(row => row.RemoteAddress));
        Assert.Contains("v 1 konverzáciách", traffic.TotalText);

        traffic.Show([new TrafficEntry { Transport = "Tcp", Protocol = "HTTP", RemoteAddress = "192.0.2.80", ServicePort = 80, BytesSent = 1 }]);
        Assert.Equal("ICMP", traffic.SelectedTransport);
        Assert.Empty(traffic.Rows);
        Assert.Equal("Filtru nezodpovedá žiadna komunikácia.", traffic.TotalText);

        traffic.ClearFilter();
        Assert.Equal(("Komunikácia (1)", false), (traffic.Header, traffic.IsFiltered));
    }

    [Fact]
    public async Task ResetTraffic_EmptiesTable_ThenShowsOnlyTrafficSinceReset_UntilShowAll()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01", MonitoringMode = MonitoringMode.Agent, IsEnabled = true, CaptureTraffic = true });
        _server.Traffic["device-1"] = [new TrafficEntry { Transport = "Tcp", Protocol = "TLS", RemoteAddress = "192.0.2.10", ServicePort = 443, BytesSent = 5 }];
        var viewModel = new MainWindowViewModel(_server, _confirmation, clock);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        viewModel.SelectedDeviceTraffic!.FilterText = "TLS";
        Assert.Single(viewModel.SelectedDeviceTraffic.Rows);
        Assert.False(viewModel.ShowAllTrafficCommand.CanExecute(null));

        await viewModel.ResetTrafficCommand.ExecuteAsync();

        Assert.Empty(viewModel.SelectedDeviceTraffic.Rows);
        Assert.True(viewModel.IsTrafficReset);
        Assert.Contains(now.ToLocalTime().ToString("HH:mm:ss"), viewModel.TrafficSinceText);
        Assert.Equal("TLS", viewModel.SelectedDeviceTraffic.FilterText);

        clock.Advance(TimeSpan.FromSeconds(90));
        await viewModel.LoadSelectedDeviceTrafficAsync(clock.GetUtcNow());

        Assert.Equal(("device-1", now, now.AddSeconds(90)), _server.TrafficRequests[^1]);
        Assert.Single(viewModel.SelectedDeviceTraffic.Rows);

        await viewModel.ShowAllTrafficCommand.ExecuteAsync();

        Assert.False(viewModel.IsTrafficReset);
        Assert.Equal(now.AddSeconds(90).AddHours(-24), _server.TrafficRequests[^1].From);
    }

    [Fact]
    public async Task SelectingAnotherDevice_CancelsTrafficReset()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01", MonitoringMode = MonitoringMode.Agent, IsEnabled = true });
        _server.Devices.Add(new DeviceInfo { Id = "device-2", Name = "PC-02", MonitoringMode = MonitoringMode.Agent, IsEnabled = true });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        await viewModel.ResetTrafficCommand.ExecuteAsync();

        viewModel.SelectedDevice = viewModel.Devices[1];

        Assert.False(viewModel.IsTrafficReset);
    }

    [Fact]
    public async Task ToggleTrafficCapture_AsksBeforeSwitchingOn_AndShowsAgentProblem()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01", MonitoringMode = MonitoringMode.Agent, IsEnabled = true });
        _server.Devices.Add(new DeviceInfo { Id = "device-2", Name = "Router", MonitoringMode = MonitoringMode.Agentless, IsEnabled = true });
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();

        viewModel.SelectedDevice = viewModel.Devices.Single(device => device.Name == "Router");
        Assert.False(viewModel.ToggleTrafficCaptureCommand.CanExecute(null));
        Assert.Contains("len na zariadení s agentom", viewModel.TrafficCaptureStatusText);

        viewModel.SelectedDevice = viewModel.Devices.Single(device => device.Name == "PC-01");
        Assert.Equal(("Zapnúť zachytávanie", "Zachytávanie komunikácie je vypnuté."), (viewModel.ToggleTrafficCaptureText, viewModel.TrafficCaptureStatusText));

        await viewModel.ToggleTrafficCaptureCommand.ExecuteAsync();

        Assert.Contains("Wireshark", Assert.Single(_confirmation.Questions));
        Assert.True(_server.Devices[0].CaptureTraffic);
        Assert.Equal("Vypnúť zachytávanie", viewModel.ToggleTrafficCaptureText);

        _server.Devices[0].TrafficCaptureProblem = "TShark was not found.";
        await viewModel.RefreshAsync();
        Assert.Equal("Agent nemôže zachytávať: TShark was not found.", viewModel.TrafficCaptureStatusText);

        // Switching off needs no confirmation.
        await viewModel.ToggleTrafficCaptureCommand.ExecuteAsync();
        Assert.Single(_confirmation.Questions);
        Assert.False(_server.Devices[0].CaptureTraffic);
    }

    [Fact]
    public async Task History_IsReloaded_WhenPeriodChanges_ButNotWithEveryRefresh()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        var viewModel = new MainWindowViewModel(_server, _confirmation, clock);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];

        // The list is refreshed every few seconds; the history is not read again that often.
        clock.Advance(TimeSpan.FromSeconds(5));
        await viewModel.RefreshAsync();
        Assert.Single(_server.HistoryRequests);

        viewModel.SelectedHistoryPeriod = viewModel.HistoryPeriods[2];
        Assert.Equal(2, _server.HistoryRequests.Count);
        Assert.Equal(clock.GetUtcNow().AddDays(-7), _server.HistoryRequests[1].From);

        clock.Advance(TimeSpan.FromSeconds(31));
        await viewModel.RefreshAsync();
        Assert.Equal(3, _server.HistoryRequests.Count);
    }

    [Fact]
    public async Task ReloadedHistory_KeepsEventList_WhenEventsDidNotChange()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.History["device-1"] = HistoryWithOneEvent("first");
        var viewModel = new MainWindowViewModel(_server, _confirmation, clock);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        var shownEvents = viewModel.SelectedDeviceHistory!.Events;

        _server.History["device-1"] = HistoryWithOneEvent("first");
        clock.Advance(TimeSpan.FromMinutes(1));
        await viewModel.RefreshAsync();
        Assert.Same(shownEvents, viewModel.SelectedDeviceHistory!.Events);

        _server.History["device-1"] = HistoryWithOneEvent("second");
        clock.Advance(TimeSpan.FromMinutes(1));
        await viewModel.RefreshAsync();
        Assert.Equal("second", Assert.Single(viewModel.SelectedDeviceHistory!.Events).Message);

        DeviceHistoryReply HistoryWithOneEvent(string message) => new()
        {
            Events = { new EventInfo { OccurredAt = Timestamp.FromDateTimeOffset(now), Type = "PortOpened", Message = message } },
        };
    }

    [Fact]
    public async Task SelectingAnotherDevice_ClearsHistoryOfThePreviousOne()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        _server.Devices.Add(new DeviceInfo { Id = "device-2", Name = "PC-02" });
        _server.History["device-1"] = new DeviceHistoryReply { AvailabilityPercent = 50 };
        _server.History["device-2"] = new DeviceHistoryReply { AvailabilityPercent = 100 };
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();

        viewModel.SelectedDevice = viewModel.Devices[0];
        Assert.Equal("50 %", viewModel.SelectedDeviceHistory!.AvailabilityText);

        viewModel.SelectedDevice = viewModel.Devices[1];
        Assert.Equal("100 %", viewModel.SelectedDeviceHistory!.AvailabilityText);

        viewModel.SelectedDevice = null;
        Assert.Null(viewModel.SelectedDeviceHistory);
    }

    [Fact]
    public void HistoryCharts_PlacePointsInThePeriod_AndBreakTheLineWhereTheDeviceWasNotReporting()
    {
        var from = new DateTimeOffset(2026, 10, 9, 11, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);
        var reply = new DeviceHistoryReply
        {
            ResourcePoints =
            {
                Point(0, cpu: 10),
                Point(1, cpu: 20),
                Point(2, cpu: 30),
                // Half an hour without reports.
                Point(30, cpu: 40),
                Point(31, cpu: null),
            },
        };

        var history = new DeviceHistoryViewModel(reply, from, to);

        Assert.Equal(5, history.CpuPoints.Count);
        Assert.Equal(new ChartPoint(0, 10), history.CpuPoints[0]);
        Assert.Null(history.CpuPoints[3]);
        Assert.Equal(new ChartPoint(0.5, 40), history.CpuPoints[4]);

        // Memory was measured in every report: 4 of 16 units is a quarter.
        Assert.Equal(6, history.MemoryPoints.Count);
        Assert.All(history.MemoryPoints.Where(point => point is not null), point => Assert.Equal(25, point!.Value.Y));
        Assert.True(history.HasCharts);

        ResourcePointInfo Point(int minute, double? cpu)
        {
            var point = new ResourcePointInfo
            {
                RecordedAt = Timestamp.FromDateTimeOffset(from.AddMinutes(minute)),
                MemoryUsedBytes = 4,
                MemoryTotalBytes = 16,
            };

            if (cpu is not null)
            {
                point.CpuUsagePercent = cpu.Value;
            }

            return point;
        }
    }

    [Theory]
    [InlineData(42, "42 s")]
    [InlineData(312, "5 min 12 s")]
    [InlineData(7500, "2 h 5 min")]
    [InlineData(273600, "3 d 4 h")]
    public void Durations_AreShownWithTheirTwoLargestUnits(int seconds, string expected)
    {
        Assert.Equal(expected, DeviceHistoryViewModel.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public async Task SaveSettingsCommand_IsDisabled_ForTextThatIsNotAWholeNumber(string text)
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        Assert.True(viewModel.SaveSettingsCommand.CanExecute(null));

        viewModel.RetentionDaysText = text;

        Assert.False(viewModel.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveSettingsCommand_SendsOnlyChangedValues_AndConfirms()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();

        await viewModel.SaveSettingsCommand.ExecuteAsync();
        Assert.Equal("Nastavenia sa nezmenili.", viewModel.StatusMessage);

        viewModel.SyncIntervalText = "30";
        viewModel.RetentionDaysText = "90";
        await viewModel.SaveSettingsCommand.ExecuteAsync();

        Assert.Equal((30, 3, 90), (_server.SyncIntervalSeconds, _server.OfflineAfterMissedSyncs, _server.RetentionDays));
        Assert.Equal(("30", "90"), (viewModel.SyncIntervalText, viewModel.RetentionDaysText));
        Assert.Contains("interval 30 s", viewModel.StatusMessage);
        Assert.Contains("história 90 dní", viewModel.StatusMessage);
        Assert.Contains("offline po 1 min 30 s", viewModel.SettingsHint);
    }

    [Fact]
    public async Task SaveSettingsCommand_WhenServerRejectsValue_ShowsReason_AndKeepsWhatUserTyped()
    {
        var viewModel = new MainWindowViewModel(_server, _confirmation);
        await viewModel.RefreshAsync();
        viewModel.SyncIntervalText = "1";
        _server.Failure = new ServerClientException(ServerErrorKind.InvalidInput, "Interval must be between 5 and 86400 seconds.");

        await viewModel.SaveSettingsCommand.ExecuteAsync();

        Assert.Contains("between 5 and 86400", viewModel.StatusMessage);
        Assert.Equal(60, _server.SyncIntervalSeconds);
        Assert.Equal("1", viewModel.SyncIntervalText);
    }

    internal sealed class FakeServerClient : IServerClient
    {
        /// <summary>The password the fake server accepts, for any user name.</summary>
        public string Password { get; set; } = "correct-password";

        public List<string> LoginAttempts { get; } = [];

        public bool IsSignedIn { get; private set; }

        public Task<LoginReply> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            LoginAttempts.Add(userName);

            if (password != Password)
            {
                throw new ServerClientException(ServerErrorKind.NotSignedIn, "The user name or password is not correct.");
            }

            IsSignedIn = true;

            return Task.FromResult(new LoginReply { Token = "token", UserName = userName });
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            IsSignedIn = false;

            return Task.CompletedTask;
        }

        /// <summary>Commands by device id, the newest first, as the fake server holds them.</summary>
        public Dictionary<string, List<CommandInfo>> Commands { get; } = [];

        public List<(string DeviceId, string Type, string Parameter)> SentCommands { get; } = [];

        public Task SendCommandAsync(string deviceId, string type, string parameter, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            SentCommands.Add((deviceId, type, parameter));

            if (!Commands.TryGetValue(deviceId, out var commands))
            {
                Commands[deviceId] = commands = [];
            }

            commands.Insert(0, new CommandInfo
            {
                Id = "command-" + SentCommands.Count,
                Type = type,
                Parameter = parameter,
                Status = "Pending",
                RequestedBy = "admin",
            });

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CommandInfo>> GetDeviceCommandsAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            return Task.FromResult<IReadOnlyList<CommandInfo>>(
                Commands.TryGetValue(deviceId, out var commands) ? commands.Select(command => command.Clone()).ToList() : []);
        }

        public List<UserInfo> Users { get; } = [];

        public List<RoleInfo> Roles { get; } = [];

        /// <summary>Passwords the fake server was given, by user name or id.</summary>
        public Dictionary<string, string> Passwords { get; } = [];

        public Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            if (currentPassword != Password)
            {
                throw new ServerClientException(ServerErrorKind.InvalidInput, "The current password is not correct.");
            }

            Password = newPassword;
            IsSignedIn = false;

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserInfo>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            return Task.FromResult<IReadOnlyList<UserInfo>>(Users.Select(user => user.Clone()).ToList());
        }

        public Task<ListRolesReply> GetRolesAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            var reply = new ListRolesReply { Permissions = { "ViewDevices", "ManageDevices", "ManageSettings", "ManageUsers" } };
            reply.Roles.AddRange(Roles.Select(role => role.Clone()));

            return Task.FromResult(reply);
        }

        public Task CreateUserAsync(string userName, string password, IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            var user = new UserInfo { Id = "user-" + (Users.Count + 1), UserName = userName, IsActive = true };
            user.RoleIds.AddRange(roleIds);
            Users.Add(user);
            Passwords[userName] = password;

            return Task.CompletedTask;
        }

        public Task SetUserActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            Users.Single(user => user.Id == userId).IsActive = isActive;

            return Task.CompletedTask;
        }

        public Task SetUserRolesAsync(string userId, IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            var user = Users.Single(user => user.Id == userId);
            user.RoleIds.Clear();
            user.RoleIds.AddRange(roleIds);

            return Task.CompletedTask;
        }

        public Task ResetUserPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            Passwords[userId] = newPassword;

            return Task.CompletedTask;
        }

        public Task CreateRoleAsync(string name, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            Roles.Add(new RoleInfo { Id = "role-" + (Roles.Count + 1), Name = name });

            return Task.CompletedTask;
        }

        public Task DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            Roles.RemoveAll(role => role.Id == roleId);

            foreach (var user in Users)
            {
                user.RoleIds.Remove(roleId);
            }

            return Task.CompletedTask;
        }

        public Task AddAccessRuleAsync(string roleId, string permission, string deviceId, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            Roles.Single(role => role.Id == roleId).Rules.Add(new AccessRuleInfo { Permission = permission, DeviceId = deviceId });

            return Task.CompletedTask;
        }

        public Task RemoveAccessRuleAsync(string roleId, string permission, string deviceId, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            var rules = Roles.Single(role => role.Id == roleId).Rules;
            rules.Remove(rules.Single(rule => rule.Permission == permission && rule.DeviceId == deviceId));

            return Task.CompletedTask;
        }

        public List<DeviceInfo> Devices { get; } = [];

        public List<(string Name, string IpAddress, int? TcpPort)> AddedDevices { get; } = [];

        public List<string> RemovedDeviceIds { get; } = [];

        /// <summary>Resources by device id; a device without an entry has none.</summary>
        public Dictionary<string, DeviceResourcesReply> Resources { get; } = [];

        /// <summary>Processes, ports and connections by device id; a device without an entry has none.</summary>
        public Dictionary<string, DeviceActivityReply> Activity { get; } = [];

        /// <summary>History by device id; a device without an entry has an empty one.</summary>
        public Dictionary<string, DeviceHistoryReply> History { get; } = [];

        public List<(string Id, DateTimeOffset From, DateTimeOffset To)> HistoryRequests { get; } = [];

        public int SyncIntervalSeconds { get; private set; } = 60;

        /// <summary>When set, every call fails with this exception.</summary>
        public ServerClientException? Failure { get; set; }

        public Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            return Task.FromResult<IReadOnlyList<DeviceInfo>>(Devices.ToList());
        }

        public Task AddAgentlessDeviceAsync(string name, string ipAddress, int? tcpPort = null, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            AddedDevices.Add((name, ipAddress, tcpPort));
            Devices.Add(new DeviceInfo { Name = name, IpAddress = ipAddress, MonitoringMode = MonitoringMode.Agentless });

            return Task.CompletedTask;
        }

        public Task<DeviceResourcesReply> GetDeviceResourcesAsync(string id, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            return Task.FromResult(Resources.TryGetValue(id, out var resources) ? resources : new DeviceResourcesReply { HasData = false });
        }

        public Task<DeviceActivityReply> GetDeviceActivityAsync(string id, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            return Task.FromResult(Activity.TryGetValue(id, out var activity) ? activity : new DeviceActivityReply());
        }

        public Task<DeviceHistoryReply> GetDeviceHistoryAsync(string id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            HistoryRequests.Add((id, from, to));

            return Task.FromResult(History.TryGetValue(id, out var history) ? history : new DeviceHistoryReply());
        }

        public Task RemoveDeviceAsync(string id, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            RemovedDeviceIds.Add(id);
            Devices.RemoveAll(device => device.Id == id);

            return Task.CompletedTask;
        }

        public Task<DeviceInfo> SetDeviceEnabledAsync(string id, bool isEnabled, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            var device = Devices.Single(device => device.Id == id);
            device.IsEnabled = isEnabled;

            return Task.FromResult(device);
        }

        public Dictionary<string, List<TrafficEntry>> Traffic { get; } = [];

        public Task<DeviceInfo> SetDeviceTrafficCaptureAsync(string id, bool capture, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            var device = Devices.Single(device => device.Id == id);
            device.CaptureTraffic = capture;

            return Task.FromResult(device);
        }

        public List<(string Id, DateTimeOffset From, DateTimeOffset To)> TrafficRequests { get; } = [];

        public Task<IReadOnlyList<TrafficEntry>> GetDeviceTrafficAsync(string id, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            TrafficRequests.Add((id, from, to));

            return Task.FromResult<IReadOnlyList<TrafficEntry>>(Traffic.TryGetValue(id, out var entries) ? entries : []);
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

        public int OfflineAfterMissedSyncs { get; private set; } = 3;

        public int RetentionDays { get; private set; } = 30;

        public Task<MonitoringSettingsInfo> UpdateOfflineThresholdAsync(int missedSyncs, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            OfflineAfterMissedSyncs = missedSyncs;

            return Task.FromResult(CreateSettings());
        }

        public Task<MonitoringSettingsInfo> UpdateRetentionAsync(int days, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            RetentionDays = days;

            return Task.FromResult(CreateSettings());
        }

        private MonitoringSettingsInfo CreateSettings()
        {
            return new MonitoringSettingsInfo
            {
                SyncIntervalSeconds = SyncIntervalSeconds,
                OfflineAfterMissedSyncs = OfflineAfterMissedSyncs,
                MinSyncIntervalSeconds = 5,
                MaxSyncIntervalSeconds = 86400,
                RetentionDays = RetentionDays,
                MaxOfflineAfterMissedSyncs = 100,
                MaxRetentionDays = 3650,
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