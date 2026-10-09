using Microsoft.Extensions.DependencyInjection;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Infrastructure.Devices;

namespace NetworkMonitoringSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceRepository, InMemoryDeviceRepository>();

        return services;
    }
}
