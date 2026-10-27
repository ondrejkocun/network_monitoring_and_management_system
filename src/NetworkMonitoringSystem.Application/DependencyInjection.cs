using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Commands;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;

namespace NetworkMonitoringSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IDeviceHistoryService, DeviceHistoryService>();
        services.AddScoped<IEventLogService, EventLogService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAccessManagementService, AccessManagementService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IRemoteCommandService, RemoteCommandService>();
        services.AddScoped<AvailabilityRecorder>();
        services.AddScoped<DeviceActivityRecorder>();
        services.AddScoped<IDeviceAvailabilityMonitor, DeviceAvailabilityMonitor>();
        services.AddScoped<IAgentlessDeviceChecker, AgentlessDeviceChecker>();
        services.AddScoped<IMonitoringSettingsService, MonitoringSettingsService>();
        services.AddScoped<IHistoryRetentionService, HistoryRetentionService>();

        return services;
    }
}
