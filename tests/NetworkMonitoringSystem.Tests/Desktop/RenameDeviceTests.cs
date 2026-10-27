namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public class RenameDeviceTests
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();
    private readonly TextInput _textInput = new();
    private readonly MainWindowViewModel _viewModel;

    public RenameDeviceTests()
    {
        _server.Devices.Add(new DeviceInfo { Id = "pc", Name = "DESKTOP-TKKTAJ2", MonitoringMode = MonitoringMode.Agent, IsEnabled = true });
        _viewModel = new MainWindowViewModel(_server, new NoConfirmation(), textInput: _textInput);
    }

    [Fact]
    public async Task Rename_OffersTheCurrentName_AndShowsTheNewOneInTheList()
    {
        await SelectAsync();
        _textInput.Answer = "  Učebňa 3 - PC 1 ";

        await _viewModel.RenameDeviceCommand.ExecuteAsync();

        Assert.Equal(("DESKTOP-TKKTAJ2", MainWindowViewModel.MaxDeviceNameLength), (_textInput.InitialText, _textInput.MaxLength));
        Assert.Equal("Učebňa 3 - PC 1", Assert.Single(_viewModel.Devices).Name);
        Assert.Equal("Učebňa 3 - PC 1", _viewModel.SelectedDevice!.Name);
        Assert.Contains("bolo premenované na „Učebňa 3 - PC 1“", _viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("DESKTOP-TKKTAJ2")]
    public async Task Rename_ChangesNothing_WhenCancelledEmptyOrTheSame(string? answer)
    {
        await SelectAsync();
        _textInput.Answer = answer;

        await _viewModel.RenameDeviceCommand.ExecuteAsync();

        Assert.Equal("DESKTOP-TKKTAJ2", _server.Devices[0].Name);
        Assert.DoesNotContain("premenované", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task Rename_RefusedByServer_ShowsWhy()
    {
        await SelectAsync();
        _textInput.Answer = "Učebňa 3";
        _server.Failure = new ServerClientException(ServerErrorKind.AccessDenied, "denied");

        await _viewModel.RenameDeviceCommand.ExecuteAsync();

        Assert.Equal("Na túto operáciu nemáte oprávnenie.", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task Rename_NeedsASelectedDevice_AndADialog()
    {
        await _viewModel.RefreshAsync();
        Assert.False(_viewModel.RenameDeviceCommand.CanExecute(null));

        _viewModel.SelectedDevice = _viewModel.Devices[0];
        Assert.True(_viewModel.RenameDeviceCommand.CanExecute(null));

        var withoutDialog = new MainWindowViewModel(_server, new NoConfirmation());
        await withoutDialog.RefreshAsync();
        withoutDialog.SelectedDevice = withoutDialog.Devices[0];
        Assert.False(withoutDialog.RenameDeviceCommand.CanExecute(null));
    }

    [Fact]
    public void RenameEvent_HasSlovakName()
    {
        Assert.Equal("Premenovanie zariadenia", EventRowViewModel.DescribeType("DeviceRenamed"));
    }

    private async Task SelectAsync()
    {
        await _viewModel.RefreshAsync();
        _viewModel.SelectedDevice = _viewModel.Devices[0];
    }

    private sealed class TextInput : ITextInputDialog
    {
        public string? Answer { get; set; }

        public string? InitialText { get; private set; }

        public int MaxLength { get; private set; }

        public string? AskForText(string title, string prompt, string initialText, int maxLength)
        {
            InitialText = initialText;
            MaxLength = maxLength;

            return Answer;
        }
    }

    private sealed class NoConfirmation : IConfirmationDialog
    {
        public bool Confirm(string title, string question) => false;
    }
}
