namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Desktop.ViewModels;

public class MainWindowViewModelTests
{
    [Fact]
    public void StatusMessage_RaisesPropertyChanged_WhenValueChanges()
    {
        var viewModel = new MainWindowViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.StatusMessage = "Pripojene";

        Assert.Equal("Pripojene", viewModel.StatusMessage);
        Assert.Equal([nameof(MainWindowViewModel.StatusMessage)], changedProperties);
    }

    [Fact]
    public void StatusMessage_DoesNotRaisePropertyChanged_WhenValueIsSame()
    {
        var viewModel = new MainWindowViewModel();
        var raised = false;
        viewModel.PropertyChanged += (_, _) => raised = true;

        viewModel.StatusMessage = viewModel.StatusMessage;

        Assert.False(raised);
    }
}
