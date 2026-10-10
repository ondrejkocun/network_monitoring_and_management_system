using System.Windows;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;
using NetworkMonitoringSystem.Desktop.Views;

namespace NetworkMonitoringSystem.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string ServerUrlKey = "Server:Url";

    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        _serviceProvider = services.BuildServiceProvider();

        // The application ends when the user closes a window, not when the sign-in window is replaced by the main one.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ShowSignIn(string.Empty);
    }

    /// <summary>Asks the user to sign in and then opens the main window for that session.</summary>
    private void ShowSignIn(string message)
    {
        var services = _serviceProvider!;

        var loginViewModel = services.GetRequiredService<LoginViewModel>();
        loginViewModel.Message = message;

        if (new LoginWindow(loginViewModel).ShowDialog() != true || loginViewModel.Session is null)
        {
            Shutdown();

            return;
        }

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        viewModel.ApplySession(loginViewModel.Session);

        var sessionEnded = false;
        viewModel.SessionEnded += (_, _) => sessionEnded = true;

        var window = new MainWindow(viewModel);
        window.Closed += (_, _) =>
        {
            if (sessionEnded)
            {
                ShowSignIn(viewModel.SessionEndMessage);
            }
            else
            {
                Shutdown();
            }
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var serverUrl = configuration[ServerUrlKey]
            ?? throw new InvalidOperationException($"'{ServerUrlKey}' is not set in appsettings.json.");

        // The password and the session token must never travel unencrypted.
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) || serverUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"'{ServerUrlKey}' must be an https address.");
        }

        services.AddSingleton(GrpcChannel.ForAddress(serverUrl));
        services.AddSingleton(provider => new AdminApi.AdminApiClient(provider.GetRequiredService<GrpcChannel>()));
        services.AddSingleton<IServerClient, GrpcServerClient>();
        services.AddSingleton<IConfirmationDialog, MessageBoxConfirmationDialog>();

        // Every sign-in gets a fresh form and a fresh main window, so nothing of the previous user stays on screen.
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainWindowViewModel>();
    }
}
