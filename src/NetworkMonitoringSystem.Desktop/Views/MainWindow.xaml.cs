using System.Windows;
using System.Windows.Threading;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // The device list is kept current by reloading it periodically while the window is open.
        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await _viewModel.RefreshCommand.ExecuteAsync();

        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshCommand.ExecuteAsync();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }
}
