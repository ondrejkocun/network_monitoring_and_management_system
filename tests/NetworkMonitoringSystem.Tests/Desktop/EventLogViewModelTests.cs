namespace NetworkMonitoringSystem.Tests.Desktop;

using Google.Protobuf.WellKnownTypes;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;
using NetworkMonitoringSystem.Tests.Fakes;

public class EventLogViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly MainWindowViewModelTests.FakeServerClient _server = new();
    private readonly MutableTimeProvider _clock = new(Now);

    public EventLogViewModelTests()
    {
        _server.EventLog.Events.Add(Entry("SettingsChanged", EventSeverity.Info, "Synchronization interval changed from 60 s to 30 s.", user: "admin"));
        _server.EventLog.Events.Add(Entry("AccessDenied", EventSeverity.Warning, "User 'eva' was refused RemoveDevice.", device: "Router", user: "eva"));
        _server.EventLog.Events.Add(Entry("DeviceWentOffline", EventSeverity.Warning, "Device 'PC-01' is offline.", device: "PC-01"));
    }

    [Fact]
    public async Task LoadAsync_ShowsEventsOfTheLast24Hours_InSlovak()
    {
        var viewModel = new EventLogViewModel(_server, _clock);

        await viewModel.LoadAsync();

        Assert.Equal((Now.AddHours(-24), Now), Assert.Single(_server.EventLogRequests));
        Assert.Equal(3, viewModel.Rows.Count);
        var settings = viewModel.Rows[0];
        Assert.Equal(("Zmena nastavení", "Informácia", "–", "admin"), (settings.TypeText, settings.SeverityText, settings.DeviceText, settings.UserText));
        Assert.Equal("Zamietnutý prístup", viewModel.Rows[1].TypeText);
        Assert.Equal("Udalosti: 3.", viewModel.Message);
    }

    [Fact]
    public async Task Filter_BySeverityAndText_NarrowsRows_WithoutAskingTheServerAgain()
    {
        var viewModel = new EventLogViewModel(_server, _clock);
        await viewModel.LoadAsync();

        viewModel.SelectedSeverity = "Varovanie";
        Assert.Equal(2, viewModel.Rows.Count);

        viewModel.FilterText = "eva";
        Assert.Equal("Router", Assert.Single(viewModel.Rows).DeviceText);
        Assert.Equal("Zobrazené udalosti: 1 z 3.", viewModel.Message);

        viewModel.FilterText = "zamietnutý";
        Assert.Single(viewModel.Rows);

        Assert.Single(_server.EventLogRequests);
    }

    [Fact]
    public async Task ChangingPeriod_LoadsAgain_AndTruncationIsExplained()
    {
        _server.EventLog.Truncated = true;
        var viewModel = new EventLogViewModel(_server, _clock);

        viewModel.SelectedPeriod = viewModel.Periods[3];
        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.Equal(Now.AddDays(-30), _server.EventLogRequests[0].From);
        Assert.Contains("zobrazujú sa najnovšie", viewModel.Message);
    }

    [Fact]
    public async Task LoadAsync_WithoutPermission_SaysSo()
    {
        _server.Failure = new ServerClientException(ServerErrorKind.AccessDenied, "denied");
        var viewModel = new EventLogViewModel(_server, _clock);

        await viewModel.LoadAsync();

        Assert.Empty(viewModel.Rows);
        Assert.Equal("Na zobrazenie záznamu udalostí nemáte oprávnenie.", viewModel.Message);
    }

    [Fact]
    public void MainWindow_OffersEventLog_OnlyWithItsPermission()
    {
        var viewModel = new MainWindowViewModel(_server, new NoConfirmation());
        Assert.False(viewModel.CanViewEventLog);

        viewModel.ApplySession(new LoginReply { UserName = "auditor", Permissions = { "ViewDevices", "ViewEventLog" } });

        Assert.True(viewModel.CanViewEventLog);
        Assert.False(viewModel.CanManageUsers);
    }

    private static EventLogEntry Entry(string type, EventSeverity severity, string message, string device = "", string user = "")
    {
        return new EventLogEntry
        {
            OccurredAt = Timestamp.FromDateTimeOffset(Now.AddMinutes(-5)),
            Type = type,
            Severity = severity,
            Message = message,
            DeviceName = device,
            UserName = user,
        };
    }

    private sealed class NoConfirmation : IConfirmationDialog
    {
        public bool Confirm(string title, string question) => false;
    }
}
