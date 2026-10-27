using System.Windows;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for EventLogWindow.xaml
/// </summary>
public partial class EventLogWindow : Window
{
    public EventLogWindow(EventLogViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
