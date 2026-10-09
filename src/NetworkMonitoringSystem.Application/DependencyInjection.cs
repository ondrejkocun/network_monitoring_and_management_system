using Microsoft.Extensions.DependencyInjection;
using NetworkMonitoringSystem.Application.Devices;

namespace NetworkMonitoringSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IDeviceService, DeviceService>();

        return services;
    }
}
