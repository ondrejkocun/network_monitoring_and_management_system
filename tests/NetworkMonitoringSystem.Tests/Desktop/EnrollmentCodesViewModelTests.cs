namespace NetworkMonitoringSystem.Tests.Desktop;

using Google.Protobuf.WellKnownTypes;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public class EnrollmentCodesViewModelTests
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();
    private readonly Confirmation _confirmation = new();

    [Fact]
    public async Task NewCode_IsShownOnce_WithWhatItIsFor_AndAppearsInTheList()
    {
        var viewModel = await OpenAsync(canAddDevices: true);
        viewModel.Note = "  Učebňa 3 ";

        await viewModel.CreateCommand.ExecuteAsync();

        Assert.Equal("K7QM-4XTD-9HPA", viewModel.CreatedCode);
        Assert.Contains("pre nový počítač", viewModel.CreatedCodeHint);
        Assert.Contains("len teraz", viewModel.CreatedCodeHint);
        Assert.Equal(("Učebňa 3", 24 * 60), (_server.EnrollmentCodes[0].Note, (int)(_server.EnrollmentCodes[0].ExpiresAt.ToDateTimeOffset() - _server.EnrollmentCodes[0].CreatedAt.ToDateTimeOffset()).TotalMinutes));
        Assert.Equal(string.Empty, viewModel.Note);

        var row = Assert.Single(viewModel.Codes);
        Assert.Equal(("nový počítač", "Platný"), (row.TargetText, row.StateText));
    }

    [Fact]
    public async Task Targets_AreANewComputerAndTheManagedDevicesWithAnAgent_TheChosenOneFirst()
    {
        var viewModel = await OpenAsync(canAddDevices: true, preselected: "pc-2");

        Assert.Equal(["Nový počítač", "Znova: PC-1", "Znova: PC-2"], viewModel.Targets.Select(target => target.Label));
        Assert.Equal("pc-2", viewModel.SelectedTarget.DeviceId);

        // Without the right to add devices there is no code for a new computer.
        var limited = await OpenAsync(canAddDevices: false);
        Assert.Equal(["Znova: PC-1", "Znova: PC-2"], limited.Targets.Select(target => target.Label));
    }

    [Fact]
    public async Task OnlyAValidCode_CanBeRevoked_AfterConfirmation()
    {
        _server.EnrollmentCodes.Add(Code("used", "Used", usedBy: "PC-7"));
        _server.EnrollmentCodes.Add(Code("valid", "Valid"));
        var viewModel = await OpenAsync(canAddDevices: true);

        viewModel.SelectedCode = viewModel.Codes[0];
        Assert.StartsWith("Použitý", viewModel.Codes[0].StateText);
        Assert.Contains("(PC-7)", viewModel.Codes[0].StateText);
        Assert.False(viewModel.RevokeCommand.CanExecute(null));

        viewModel.SelectedCode = viewModel.Codes[1];
        _confirmation.Answer = false;
        await viewModel.RevokeCommand.ExecuteAsync();
        Assert.Equal("Valid", _server.EnrollmentCodes[1].State);

        _confirmation.Answer = true;
        await viewModel.RevokeCommand.ExecuteAsync();
        Assert.Equal("Zrušený", viewModel.Codes[1].StateText);
    }

    [Fact]
    public async Task RefusedCode_ShowsWhy()
    {
        var viewModel = await OpenAsync(canAddDevices: true);
        _server.Failure = new ServerClientException(ServerErrorKind.InvalidInput, "Registračný kód musí platiť od 10 minút do 7 dní.");

        await viewModel.CreateCommand.ExecuteAsync();

        Assert.Equal(string.Empty, viewModel.CreatedCode);
        Assert.Contains("od 10 minút do 7 dní", viewModel.Message);
    }

    private async Task<EnrollmentCodesViewModel> OpenAsync(bool canAddDevices, string? preselected = null)
    {
        DeviceInfo Device(string id, string name, MonitoringMode mode, params string[] permissions)
        {
            var device = new DeviceInfo { Id = id, Name = name, MonitoringMode = mode, IsEnabled = true };
            device.Permissions.AddRange(permissions);

            return device;
        }

        var devices = new[]
        {
            Device("pc-2", "PC-2", MonitoringMode.Agent, "ViewDevices", "ManageDevices"),
            Device("pc-1", "PC-1", MonitoringMode.Agent, "ViewDevices", "ManageDevices"),
            Device("pc-3", "PC-3", MonitoringMode.Agent, "ViewDevices"),
            Device("router", "Router", MonitoringMode.Agentless, "ViewDevices", "ManageDevices"),
        }.Select(device => new DeviceRowViewModel(device));

        var viewModel = new EnrollmentCodesViewModel(_server, _confirmation, devices, canAddDevices, preselected);
        await viewModel.LoadAsync();

        return viewModel;
    }

    private static EnrollmentCodeInfo Code(string id, string state, string usedBy = "")
    {
        var code = new EnrollmentCodeInfo
        {
            Id = id,
            CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            ExpiresAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(1)),
            CreatedBy = "admin",
            State = state,
            UsedByHostName = usedBy,
        };

        if (state == "Used")
        {
            code.UsedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        }

        return code;
    }

    private sealed class Confirmation : IConfirmationDialog
    {
        public bool Answer { get; set; } = true;

        public bool Confirm(string title, string question) => Answer;
    }
}
