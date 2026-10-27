using System.Windows;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for SettingsWindow.xaml: the monitoring settings of the main window's view model. It opens with
/// the saved values and closes once they are saved; values the server refuses keep the form open with the reason.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public SettingsWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _viewModel.ResetSettingsForm();
        DataContext = viewModel;

        _viewModel.SettingsSaved += OnSettingsSaved;
        Closed += (_, _) => _viewModel.SettingsSaved -= OnSettingsSaved;
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        DialogResult = true;
    }
}
