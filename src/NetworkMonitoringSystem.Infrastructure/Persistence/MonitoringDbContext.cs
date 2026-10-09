using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Infrastructure.Persistence;

public sealed class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options)
        : base(options)
    {
    }

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<MonitoringSettings> MonitoringSettings => Set<MonitoringSettings>();

    public DbSet<DeviceSnapshot> DeviceSnapshots => Set<DeviceSnapshot>();

    public DbSet<Outage> Outages => Set<Outage>();

    public DbSet<MonitoringEvent> Events => Set<MonitoringEvent>();

    public DbSet<ProcessRun> ProcessRuns => Set<ProcessRun>();

    public DbSet<ListeningPort> ListeningPorts => Set<ListeningPort>();

    public DbSet<ActiveConnection> ActiveConnections => Set<ActiveConnection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MonitoringDbContext).Assembly);
    }
}
