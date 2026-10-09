using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Monitoring;

public interface IMonitoringSettingsRepository
{
    Task<MonitoringSettings> GetAsync(CancellationToken cancellationToken = default);
}
