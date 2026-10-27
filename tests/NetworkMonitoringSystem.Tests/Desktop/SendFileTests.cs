namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public sealed class SendFileTests : IDisposable
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();
    private readonly Confirmation _confirmation = new();
    private readonly Picker _picker = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nms-send-" + Guid.NewGuid().ToString("N"));
    private readonly MainWindowViewModel _viewModel;

    public SendFileTests()
    {
        Directory.CreateDirectory(_folder);
        _server.Devices.Add(new DeviceInfo { Id = "pc", Name = "PC-01", MonitoringMode = MonitoringMode.Agent });
        _server.Devices.Add(new DeviceInfo { Id = "router", Name = "Router", MonitoringMode = MonitoringMode.Agentless });
        _viewModel = new MainWindowViewModel(_server, _confirmation, filePicker: _picker);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task ChosenFile_IsSentAfterConfirmation_AndShownInTheCommandList()
    {
        _picker.Path = CreateFile("config.json", [1, 2, 3, 4, 5]);
        await SelectAsync("pc");

        await _viewModel.SendFileCommand.ExecuteAsync();

        var question = Assert.Single(_confirmation.Questions);
        Assert.Contains("„config.json“ (5 B)", question);
        Assert.Contains("Inbox", question);
        var sent = Assert.Single(_server.SentFiles);
        Assert.Equal(("pc", "config.json"), (sent.DeviceId, sent.FileName));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, sent.Content);
        Assert.Contains("bol odoslaný", _viewModel.CommandMessage);

        var row = Assert.Single(_viewModel.DeviceCommands);
        Assert.Equal(("Poslanie súboru", "config.json"), (row.CommandText, row.Parameter));
        Assert.Same(row, _viewModel.SelectedCommandRow);
    }

    [Fact]
    public async Task NothingIsSent_WhenTheUserCancelsThePickerOrDeclines()
    {
        await SelectAsync("pc");

        _picker.Path = null;
        await _viewModel.SendFileCommand.ExecuteAsync();
        Assert.Empty(_confirmation.Questions);

        _picker.Path = CreateFile("a.txt", [1]);
        _confirmation.Answer = false;
        await _viewModel.SendFileCommand.ExecuteAsync();

        Assert.Single(_confirmation.Questions);
        Assert.Empty(_server.SentFiles);
    }

    [Fact]
    public async Task FileOverTheLimit_IsRefusedBeforeUploading()
    {
        var path = Path.Combine(_folder, "big.iso");

        using (var file = File.Create(path))
        {
            file.SetLength(MainWindowViewModel.MaxFileSize + 1);
        }

        _picker.Path = path;
        await SelectAsync("pc");

        await _viewModel.SendFileCommand.ExecuteAsync();

        Assert.Empty(_confirmation.Questions);
        Assert.Empty(_server.SentFiles);
        Assert.Contains("najviac 100 MB", _viewModel.CommandMessage);
    }

    [Fact]
    public async Task ServerRefusal_IsShown()
    {
        _picker.Path = CreateFile("a.txt", [1]);
        await SelectAsync("pc");
        _server.Failure = new ServerClientException(ServerErrorKind.AccessDenied, "denied");

        await _viewModel.SendFileCommand.ExecuteAsync();

        Assert.Equal("Na túto operáciu nemáte oprávnenie.", _viewModel.CommandMessage);
    }

    [Fact]
    public async Task Files_CanBeSentOnlyToDeviceWithAgent_AndOnlyWithAPicker()
    {
        await SelectAsync("router");
        Assert.False(_viewModel.SendFileCommand.CanExecute(null));

        await SelectAsync("pc");
        Assert.True(_viewModel.SendFileCommand.CanExecute(null));

        var withoutPicker = new MainWindowViewModel(_server, _confirmation);
        await withoutPicker.RefreshAsync();
        withoutPicker.SelectedDevice = withoutPicker.Devices.Single(device => device.Id == "pc");
        Assert.False(withoutPicker.SendFileCommand.CanExecute(null));
    }

    [Fact]
    public void SendFile_HasSlovakNames_InCommandListAndPermissions()
    {
        Assert.Equal("Poslanie súboru", CommandOption.Describe("SendFile"));
        Assert.Equal("Príkaz: Poslanie súboru", PermissionOption.Describe("SendFile"));
        Assert.DoesNotContain(CommandOption.All, option => option.Name == "SendFile");
    }

    private async Task SelectAsync(string deviceId)
    {
        await _viewModel.RefreshAsync();
        _viewModel.SelectedDevice = _viewModel.Devices.Single(device => device.Id == deviceId);
    }

    private string CreateFile(string name, byte[] content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);

        return path;
    }

    private sealed class Picker : IFilePicker
    {
        public string? Path { get; set; }

        public string? PickFileToSend() => Path;
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
