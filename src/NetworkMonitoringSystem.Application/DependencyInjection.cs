using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;

namespace NetworkMonitoringSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<AvailabilityRecorder>();
        services.AddScoped<IDeviceAvailabilityMonitor, DeviceAvailabilityMonitor>();
        services.AddScoped<IAgentlessDeviceChecker, AgentlessDeviceChecker>();
        services.AddScoped<IMonitoringSettingsService, MonitoringSettingsService>();

        return services;
    }
}
