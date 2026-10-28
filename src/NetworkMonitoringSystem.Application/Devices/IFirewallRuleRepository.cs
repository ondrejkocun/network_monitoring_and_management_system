using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Application.Devices;

/// <summary>Storage for firewall rules. Changes are saved through <see cref="IUnitOfWork"/>.</summary>
public interface IFirewallRuleRepository
{
    /// <summary>The rules of the device in the order they were created.</summary>
    Task<IReadOnlyList<FirewallRule>> GetByDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);

    Task<FirewallRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(FirewallRule rule);

    void Remove(FirewallRule rule);
}
