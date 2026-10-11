namespace NetworkMonitoringSystem.Tests.Desktop;

using Google.Protobuf.WellKnownTypes;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public class DeviceCommandsTests
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();
    private readonly Confirmation _confirmation = new();
    private readonly MainWindowViewModel _viewModel;

    public DeviceCommandsTests()
    {
        _server.Devices.Add(new DeviceInfo { Id = "pc", Name = "PC-01", MonitoringMode = MonitoringMode.Agent });
        _server.Devices.Add(new DeviceInfo { Id = "router", Name = "Router", MonitoringMode = MonitoringMode.Agentless });
        _viewModel = new MainWindowViewModel(_server, _confirmation);
    }

    [Fact]
    public async Task Commands_CanBeSentOnlyToDeviceWithAgent()
    {
        await _viewModel.RefreshAsync();
        Assert.False(_viewModel.CanSendCommands);
        Assert.False(_viewModel.SendCommandCommand.CanExecute(null));

        _viewModel.SelectedDevice = _viewModel.Devices.Single(device => device.Id == "router");
        Assert.False(_viewModel.CanSendCommands);
        Assert.Contains("len zariadeniu s agentom", _viewModel.CommandMessage);

        _viewModel.SelectedDevice = _viewModel.Devices.Single(device => device.Id == "pc");
        Assert.True(_viewModel.CanSendCommands);
        Assert.Equal(string.Empty, _viewModel.CommandMessage);
    }

    [Fact]
    public async Task CommandWithParameter_NeedsIt_AndSendsItTrimmed()
    {
        await SelectComputerAsync();

        // Ping is offered first and needs a target.
        Assert.Equal(("Ping", "Cieľová adresa", true), (_viewModel.SelectedCommand.Name, _viewModel.CommandParameterLabel, _viewModel.IsCommandParameterNeeded));
        Assert.False(_viewModel.SendCommandCommand.CanExecute(null));

        _viewModel.CommandParameter = "  192.168.50.1 ";
        Assert.True(_viewModel.SendCommandCommand.CanExecute(null));

        await _viewModel.SendCommandCommand.ExecuteAsync();

        Assert.Equal(("pc", "Ping", "192.168.50.1"), Assert.Single(_server.SentCommands));
        Assert.Empty(_confirmation.Questions);
        Assert.Contains("bol odoslaný", _viewModel.CommandMessage);

        // The command appears in the list at once and is selected, waiting for the agent.
        var shown = Assert.Single(_viewModel.DeviceCommands);
        Assert.Equal(("Ping zo zariadenia", "192.168.50.1", "admin", "Čaká na agenta"), (shown.CommandText, shown.Parameter, shown.RequestedBy, shown.StatusText));
        Assert.Same(shown, _viewModel.SelectedCommandRow);
        Assert.Equal("Príkazy (1)", _viewModel.CommandsHeader);
    }

    [Fact]
    public async Task ChangingCommand_ClearsTheParameter_AndCommandWithoutOne_SendsNone()
    {
        await SelectComputerAsync();
        _viewModel.CommandParameter = "1234";

        _viewModel.SelectedCommand = _viewModel.CommandOptions.Single(command => command.Name == "SynchronizeNow");

        Assert.Equal(string.Empty, _viewModel.CommandParameter);
        Assert.False(_viewModel.IsCommandParameterNeeded);
        Assert.True(_viewModel.SendCommandCommand.CanExecute(null));

        await _viewModel.SendCommandCommand.ExecuteAsync();

        Assert.Equal(("pc", "SynchronizeNow", string.Empty), Assert.Single(_server.SentCommands));
    }

    [Theory]
    [InlineData("RestartDevice")]
    [InlineData("ShutDownDevice")]
    [InlineData("KillProcess")]
    [InlineData("StopService")]
    [InlineData("RestartService")]
    public async Task DisruptiveCommand_IsSentOnlyAfterConfirmation(string commandName)
    {
        await SelectComputerAsync();
        _viewModel.SelectedCommand = _viewModel.CommandOptions.Single(command => command.Name == commandName);
        _viewModel.CommandParameter = _viewModel.IsCommandParameterNeeded ? "1234" : string.Empty;

        _confirmation.Answer = false;
        await _viewModel.SendCommandCommand.ExecuteAsync();
        Assert.Empty(_server.SentCommands);
        Assert.Contains("PC-01", Assert.Single(_confirmation.Questions));

        _confirmation.Answer = true;
        await _viewModel.SendCommandCommand.ExecuteAsync();
        Assert.Equal(commandName, Assert.Single(_server.SentCommands).Type);
    }

    [Fact]
    public async Task RefusedCommand_ShowsTheReason_AndAddsNothing()
    {
        await SelectComputerAsync();
        _viewModel.CommandParameter = "192.168.50.1";
        _server.Failure = new ServerClientException(ServerErrorKind.AccessDenied, "You do not have permission for this operation.");

        await _viewModel.SendCommandCommand.ExecuteAsync();

        Assert.Equal("Na túto operáciu nemáte oprávnenie.", _viewModel.CommandMessage);
        Assert.Empty(_viewModel.DeviceCommands);

        _server.Failure = new ServerClientException(ServerErrorKind.InvalidInput, "The device is not online.");
        await _viewModel.SendCommandCommand.ExecuteAsync();
        Assert.Contains("The device is not online.", _viewModel.CommandMessage);
    }

    [Fact]
    public async Task Refresh_ShowsTheResultWhenItArrives_AndKeepsTheListAndSelectionOtherwise()
    {
        var requestedAt = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        _server.Commands["pc"] =
        [
            new CommandInfo { Id = "c2", Type = "Ping", Parameter = "10.0.0.1", Status = "Sent", RequestedBy = "jana", RequestedAt = Timestamp.FromDateTimeOffset(requestedAt) },
            new CommandInfo { Id = "c1", Type = "FutureCommand", Status = "Expired", RequestedBy = "admin", ResultMessage = "The agent did not pick the command up in time." },
        ];
        await SelectComputerAsync();

        Assert.Equal(["Vykonáva sa", "Vypršal"], _viewModel.DeviceCommands.Select(command => command.StatusText));
        Assert.Equal(requestedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), _viewModel.DeviceCommands[0].RequestedText);

        // A command this version does not know is shown by the name the server gave it.
        Assert.Equal("FutureCommand", _viewModel.DeviceCommands[1].CommandText);

        _viewModel.SelectedCommandRow = _viewModel.DeviceCommands[0];
        var shownList = _viewModel.DeviceCommands;

        // Nothing changed on the server: the list on screen is not rebuilt.
        await _viewModel.RefreshAsync();
        Assert.Same(shownList, _viewModel.DeviceCommands);

        _server.Commands["pc"][0].Status = "Succeeded";
        _server.Commands["pc"][0].ResultMessage = "Ping 10.0.0.1:\n  reply in 1 ms\n4 of 4 replies received.";
        await _viewModel.RefreshAsync();

        var finished = _viewModel.DeviceCommands[0];
        Assert.Equal(("Úspešný", "Ping 10.0.0.1:"), (finished.StatusText, finished.ResultSummary));
        Assert.Contains("4 of 4 replies received.", finished.ResultMessage);

        // The same command stays selected, now with its result.
        Assert.Same(finished, _viewModel.SelectedCommandRow);
    }

    [Fact]
    public async Task SelectingAnotherDevice_ShowsItsCommands_NotThoseOfThePreviousOne()
    {
        _server.Commands["pc"] = [new CommandInfo { Id = "c1", Type = "Ping", Status = "Succeeded" }];
        await SelectComputerAsync();
        Assert.Single(_viewModel.DeviceCommands);

        _viewModel.SelectedDevice = _viewModel.Devices.Single(device => device.Id == "router");

        Assert.Empty(_viewModel.DeviceCommands);
        Assert.Equal("Príkazy", _viewModel.CommandsHeader);
        Assert.Null(_viewModel.SelectedCommandRow);
    }

    [Fact]
    public void EveryCommand_IsOfferedOnce_AndItsPermissionIsNamedInTheAccessWindow()
    {
        string[] expected =
        [
            "RestartDevice", "ShutDownDevice", "KillProcess", "StartService", "StopService",
            "RestartService", "SynchronizeNow", "Ping", "Traceroute", "FlushDnsCache",
        ];

        Assert.Equal(expected.Order(), CommandOption.All.Select(command => command.Name).Order());
        Assert.Equal("Príkaz: Reštart zariadenia", PermissionOption.Describe("RestartDevice"));
        Assert.Equal("Zobrazenie zariadení", PermissionOption.Describe("ViewDevices"));
    }

    private async Task SelectComputerAsync()
    {
        await _viewModel.RefreshAsync();
        _viewModel.SelectedDevice = _viewModel.Devices.Single(device => device.Id == "pc");
    }

    private sealed class Confirmation : IConfirmationDialog
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
