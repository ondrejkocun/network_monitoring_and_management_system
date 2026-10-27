using System.Windows;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>Interaction logic for EnrollmentCodesWindow.xaml</summary>
public partial class EnrollmentCodesWindow : Window
{
    private readonly EnrollmentCodesViewModel _viewModel;

    public EnrollmentCodesWindow(EnrollmentCodesViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_viewModel.CreatedCode);
    }
}
