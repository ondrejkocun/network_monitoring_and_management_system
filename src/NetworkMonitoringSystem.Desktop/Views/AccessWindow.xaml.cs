using System.Windows;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for AccessWindow.xaml
/// </summary>
public partial class AccessWindow : Window
{
    private readonly AccessViewModel _viewModel;

    public AccessWindow(AccessViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    // Password boxes cannot be bound, on purpose: passwords should not sit in properties.
    private async void OnCreateUserClick(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.CreateUserAsync(NewUserPasswordBox.Password))
        {
            NewUserPasswordBox.Clear();
        }
    }

    private async void OnResetPasswordClick(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.ResetPasswordAsync(ResetPasswordBox.Password))
        {
            ResetPasswordBox.Clear();
        }
    }
}
