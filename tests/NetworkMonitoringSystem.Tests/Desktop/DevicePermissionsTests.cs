namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>The desktop offers on a device only what the user holds a permission for there.</summary>
public class DevicePermissionsTests
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();

    [Fact]
    public async Task OnlyPermittedCommands_AreOffered_AndManagingTheDeviceIsNot()
    {
        var viewModel = await SelectAsync("ViewDevices", "Ping", "KillProcess");

        Assert.Equal(["Ping", "KillProcess"], viewModel.CommandOptions.Select(option => option.Name));
        Assert.True(viewModel.CanSendCommands);
        Assert.False(viewModel.SendFileCommand.CanExecute(null));
        Assert.False(viewModel.RemoveDeviceCommand.CanExecute(null));
        Assert.False(viewModel.RenameDeviceCommand.CanExecute(null));
        Assert.False(viewModel.ToggleDeviceEnabledCommand.CanExecute(null));
        Assert.False(viewModel.ToggleTrafficCaptureCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChosenCommand_StaysChosen_WhenTheListIsRefreshed()
    {
        var viewModel = await SelectAsync("ViewDevices", "Ping", "KillProcess");
        viewModel.SelectedCommand = viewModel.CommandOptions[1];
        var options = viewModel.CommandOptions;

        await viewModel.RefreshAsync();

        Assert.Same(options, viewModel.CommandOptions);
        Assert.Equal("KillProcess", viewModel.SelectedCommand.Name);
    }

    [Fact]
    public async Task WithoutAnyCommandPermission_TheUserIsToldWhy()
    {
        var viewModel = await SelectAsync("ViewDevices", "ManageDevices");

        Assert.Empty(viewModel.CommandOptions);
        Assert.False(viewModel.CanSendCommands);
        Assert.False(viewModel.SendCommandCommand.CanExecute(null));
        Assert.Equal("Na príkazy pre toto zariadenie nemáte oprávnenie.", viewModel.CommandMessage);
        Assert.True(viewModel.RemoveDeviceCommand.CanExecute(null));
    }

    [Fact]
    public async Task SendingAFile_NeedsItsOwnPermission()
    {
        var viewModel = await SelectAsync("ViewDevices", "SendFile");

        Assert.True(viewModel.SendFileCommand.CanExecute(null));
        Assert.Equal(string.Empty, viewModel.CommandMessage);
    }

    private async Task<MainWindowViewModel> SelectAsync(params string[] permissions)
    {
        var device = new DeviceInfo { Id = "pc", Name = "PC-01", MonitoringMode = MonitoringMode.Agent, IsEnabled = true };
        device.Permissions.AddRange(permissions);
        _server.Devices.Add(device);

        var viewModel = new MainWindowViewModel(_server, new NoConfirmation(), filePicker: new NoFile(), textInput: new NoText());
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];

        return viewModel;
    }

    private sealed class NoConfirmation : IConfirmationDialog
    {
        public bool Confirm(string title, string question) => false;
    }

    private sealed class NoFile : IFilePicker
    {
        public string? PickFileToSend() => null;
    }

    private sealed class NoText : ITextInputDialog
    {
        public string? AskForText(string title, string prompt, string initialText, int maxLength) => null;
    }
}
