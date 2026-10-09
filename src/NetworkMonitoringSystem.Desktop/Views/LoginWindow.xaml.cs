using System.Windows;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for LoginWindow.xaml
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += (_, _) => UserNameBox.Focus();
    }

    // A password box cannot be bound, on purpose: the password should not sit in a property.
    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        var signedIn = await _viewModel.LoginAsync(PasswordBox.Password);

        PasswordBox.Clear();

        if (signedIn)
        {
            DialogResult = true;
        }
        else
        {
            PasswordBox.Focus();
        }
    }
}
