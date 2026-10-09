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

        _serviceProvider.GetRequiredService<MainWindow>().Show();
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

        services.AddSingleton(GrpcChannel.ForAddress(serverUrl));
        services.AddSingleton(provider => new AdminApi.AdminApiClient(provider.GetRequiredService<GrpcChannel>()));
        services.AddSingleton<IServerClient, GrpcServerClient>();
        services.AddSingleton<IConfirmationDialog, MessageBoxConfirmationDialog>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
