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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MonitoringDbContext).Assembly);
    }
}
