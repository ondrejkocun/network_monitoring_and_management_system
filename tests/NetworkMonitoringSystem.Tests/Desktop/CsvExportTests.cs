namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;
using NetworkMonitoringSystem.Tests.Fakes;

public class CsvExportTests
{
    [Fact]
    public void Csv_SeparatesFieldsBySemicolons_QuotesWhatNeedsIt_AndDisarmsFormulas()
    {
        var text = Csv.Format(["Čas", "Správa"], [["14:00", "a; b"], ["14:01", "povedal \"áno\""], ["14:02", "=HYPERLINK(\"x\")"]]);

        Assert.Equal(
            "Čas;Správa\r\n14:00;\"a; b\"\r\n14:01;\"povedal \"\"áno\"\"\"\r\n14:02;\"'=HYPERLINK(\"\"x\"\")\"\r\n",
            text);
    }

    [Fact]
    public async Task Traffic_IsExportedAsShown_WithExactByteCounts_UnderTheNameOfTheDevice()
    {
        var server = new MainWindowViewModelTests.FakeServerClient();
        server.Devices.Add(new DeviceInfo { Id = "pc", Name = "PC/01", MonitoringMode = MonitoringMode.Agent, IsEnabled = true });
        server.Traffic["pc"] =
        [
            new TrafficEntry { Transport = "Udp", Protocol = "DNS", RemoteAddress = "8.8.8.8", ServicePort = 53, BytesSent = 1500, BytesReceived = 3000 },
            new TrafficEntry { Transport = "Tcp", Protocol = "TLS", RemoteAddress = "1.1.1.1", ServicePort = 443, BytesSent = 1, BytesReceived = 2 },
        ];
        var exporter = new RecordingExporter();
        var viewModel = new MainWindowViewModel(server, new NoConfirmation(), new MutableTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)), csvExporter: exporter);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        await viewModel.LoadSelectedDeviceResourcesAsync();
        viewModel.SelectedDeviceTraffic!.FilterText = "DNS";

        await viewModel.ExportTrafficCommand.ExecuteAsync();

        Assert.Equal("komunikacia-PC_01-2026-10-27", exporter.Name);
        Assert.Equal(["8.8.8.8", "53", "DNS", "UDP", "1500", "3000"], Assert.Single(exporter.Rows!));
        Assert.Equal("Exportované do C:\\export.csv.", viewModel.StatusMessage);
    }

    [Fact]
    public void WithoutExporter_NothingCanBeExported()
    {
        var viewModel = new MainWindowViewModel(new MainWindowViewModelTests.FakeServerClient(), new NoConfirmation());

        Assert.False(viewModel.ExportTrafficCommand.CanExecute(null));
        Assert.False(viewModel.CreateEventLogViewModel().ExportCommand.CanExecute(null));
    }

    private sealed class RecordingExporter : ICsvExporter
    {
        public string? Name { get; private set; }

        public IReadOnlyList<IReadOnlyList<string>>? Rows { get; private set; }

        public string? Export(string suggestedName, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
        {
            Name = suggestedName;
            Rows = rows.ToList();

            return "C:\\export.csv";
        }
    }

    private sealed class NoConfirmation : IConfirmationDialog
    {
        public bool Confirm(string title, string question) => false;
    }
}
