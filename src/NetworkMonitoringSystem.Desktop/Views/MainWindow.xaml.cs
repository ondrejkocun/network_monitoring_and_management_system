using System.Windows;
using System.Windows.Threading;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly WindowLayoutStore LayoutStore = new();

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

        // The window opens as the user left it.
        SourceInitialized += (_, _) => RestoreLayout();
        Closing += (_, _) => SaveLayout();

        // Someone watching the network has to learn about an outage even with the window in the background.
        _viewModel.DevicesWentOffline += (_, _) => TaskbarAttention.Request(this);

        // Signing out, a changed password and an expired session all end in the sign-in window, which the application shows next.
        _viewModel.SessionEnded += (_, _) =>
        {
            _refreshTimer.Stop();
            Close();
        };
    }

    private void OnAccessClick(object sender, RoutedEventArgs e)
    {
        new AccessWindow(_viewModel.CreateAccessViewModel()) { Owner = this }.ShowDialog();
    }

    private void RestoreLayout()
    {
        if (LayoutStore.Load() is not { } saved)
        {
            return;
        }

        var screens = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var layout = saved.FitTo(screens);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = layout.Left;
        Top = layout.Top;
        Width = layout.Width;
        Height = layout.Height;
        WindowState = layout.IsMaximized ? WindowState.Maximized : WindowState.Normal;

        DeviceListRow.Height = new GridLength(layout.ListShare, GridUnitType.Star);
        DetailRow.Height = new GridLength(1 - layout.ListShare, GridUnitType.Star);

        if (layout.DetailTab < DetailTabs.Items.Count)
        {
            DetailTabs.SelectedIndex = layout.DetailTab;
        }
    }

    private void SaveLayout()
    {
        // A maximized window is remembered with the size it returns to.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var rows = DeviceListRow.ActualHeight + DetailRow.ActualHeight;

        LayoutStore.Save(new WindowLayout(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            WindowState == WindowState.Maximized,
            rows > 0 ? DeviceListRow.ActualHeight / rows : 1.0 / 3,
            Math.Max(0, DetailTabs.SelectedIndex)));
    }

    private void OnFindExecuted(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        DeviceSearchBox.Focus();
        DeviceSearchBox.SelectAll();
    }

    private void OnAddDeviceClick(object sender, RoutedEventArgs e)
    {
        new AddDeviceWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        new SettingsWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    private void OnEventLogClick(object sender, RoutedEventArgs e)
    {
        new EventLogWindow(_viewModel.CreateEventLogViewModel()) { Owner = this }.ShowDialog();
    }

    private void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        if (new ChangePasswordWindow(_viewModel.CreateChangePasswordViewModel()) { Owner = this }.ShowDialog() == true)
        {
            _viewModel.EndSessionAfterPasswordChange();
        }
    }
}
