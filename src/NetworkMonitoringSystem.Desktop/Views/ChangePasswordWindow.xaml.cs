using System.Windows;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for ChangePasswordWindow.xaml
/// </summary>
public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _viewModel;

    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += (_, _) => CurrentPasswordBox.Focus();
    }

    // Password boxes cannot be bound, on purpose: passwords should not sit in properties.
    private async void OnChangeClick(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.ChangeAsync(CurrentPasswordBox.Password, NewPasswordBox.Password, RepeatedPasswordBox.Password))
        {
            DialogResult = true;
        }
        else
        {
            CurrentPasswordBox.Clear();
            CurrentPasswordBox.Focus();
        }
    }
}
