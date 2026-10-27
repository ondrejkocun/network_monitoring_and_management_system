using System.Windows;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for AddDeviceWindow.xaml: the form of the main window's view model for adding a device without
/// an agent. It closes once the device has been added; a refused one keeps the form open with the reason.
/// </summary>
public partial class AddDeviceWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public AddDeviceWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _viewModel.ClearNewDevice();
        DataContext = viewModel;

        _viewModel.DeviceAdded += OnDeviceAdded;
        Closed += (_, _) => _viewModel.DeviceAdded -= OnDeviceAdded;
    }

    private void OnDeviceAdded(object? sender, EventArgs e)
    {
        DialogResult = true;
    }
}
