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

        public List<DeviceInfo> Devices { get; } = [];

        public List<(string Name, string IpAddress)> AddedDevices { get; } = [];

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

        public Task AddAgentlessDeviceAsync(string name, string ipAddress, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();

            AddedDevices.Add((name, ipAddress));
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