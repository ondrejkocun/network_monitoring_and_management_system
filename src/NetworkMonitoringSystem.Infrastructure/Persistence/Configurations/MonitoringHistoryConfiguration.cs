using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class DeviceSnapshotConfiguration : IEntityTypeConfiguration<DeviceSnapshot>
{
    public void Configure(EntityTypeBuilder<DeviceSnapshot> builder)
    {
        builder.HasKey(snapshot => snapshot.Id);

        builder.Property(snapshot => snapshot.DeviceId);
        builder.Property(snapshot => snapshot.RecordedAt);
        builder.Property(snapshot => snapshot.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(snapshot => snapshot.ResponseTimeMs);
        builder.Property(snapshot => snapshot.HasResources);
        builder.Property(snapshot => snapshot.CpuUsagePercent);
        builder.Property(snapshot => snapshot.MemoryTotalBytes);
        builder.Property(snapshot => snapshot.MemoryUsedBytes);

        builder.HasMany(snapshot => snapshot.Disks).WithOne().HasForeignKey("SnapshotId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(snapshot => snapshot.NetworkInterfaces).WithOne().HasForeignKey("SnapshotId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(snapshot => snapshot.Disks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(snapshot => snapshot.NetworkInterfaces).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(snapshot => snapshot.ProcessUsages).WithOne().HasForeignKey("SnapshotId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(snapshot => snapshot.ProcessUsages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Device>().WithMany().HasForeignKey(snapshot => snapshot.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(snapshot => new { snapshot.DeviceId, snapshot.RecordedAt });
    }
}

internal sealed class DiskUsageConfiguration : IEntityTypeConfiguration<DiskUsage>
{
    public void Configure(EntityTypeBuilder<DiskUsage> builder)
    {
        builder.ToTable("DiskSnapshots");
        builder.HasKey(disk => disk.Id);

        builder.Property(disk => disk.Name).HasMaxLength(ResourceUsage.MaxNameLength).IsRequired();
        builder.Property(disk => disk.TotalBytes);
        builder.Property(disk => disk.FreeBytes);
    }
}

internal sealed class NetworkInterfaceUsageConfiguration : IEntityTypeConfiguration<NetworkInterfaceUsage>
{
    public void Configure(EntityTypeBuilder<NetworkInterfaceUsage> builder)
    {
        builder.ToTable("NetworkInterfaceSnapshots");
        builder.HasKey(networkInterface => networkInterface.Id);

        builder.Property(networkInterface => networkInterface.Name).HasMaxLength(ResourceUsage.MaxNameLength).IsRequired();
        builder.Property(networkInterface => networkInterface.MacAddress).HasMaxLength(ResourceUsage.MaxAddressLength);
        builder.Property(networkInterface => networkInterface.IpAddress).HasMaxLength(ResourceUsage.MaxAddressLength);
        builder.Property(networkInterface => networkInterface.IsUp);
        builder.Property(networkInterface => networkInterface.BytesSent);
        builder.Property(networkInterface => networkInterface.BytesReceived);
    }
}

internal sealed class OutageConfiguration : IEntityTypeConfiguration<Outage>
{
    public void Configure(EntityTypeBuilder<Outage> builder)
    {
        builder.HasKey(outage => outage.Id);

        builder.Property(outage => outage.DeviceId);
        builder.Property(outage => outage.StartedAt);
        builder.Property(outage => outage.EndedAt);
        builder.Ignore(outage => outage.IsOngoing);

        builder.HasOne<Device>().WithMany().HasForeignKey(outage => outage.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(outage => new { outage.DeviceId, outage.StartedAt });
    }
}

internal sealed class MonitoringEventConfiguration : IEntityTypeConfiguration<MonitoringEvent>
{
    public void Configure(EntityTypeBuilder<MonitoringEvent> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(monitoringEvent => monitoringEvent.Id);

        builder.Property(monitoringEvent => monitoringEvent.OccurredAt);
        builder.Property(monitoringEvent => monitoringEvent.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(monitoringEvent => monitoringEvent.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(monitoringEvent => monitoringEvent.Message).HasMaxLength(1000).IsRequired();
        builder.Property(monitoringEvent => monitoringEvent.DeviceId);

        // Events are kept when a device is removed; they only lose the link to it.
        builder.HasOne<Device>().WithMany().HasForeignKey(monitoringEvent => monitoringEvent.DeviceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(monitoringEvent => monitoringEvent.OccurredAt);
        builder.HasIndex(monitoringEvent => monitoringEvent.DeviceId);

        // Events are kept when a user is removed as well.
        builder.Property(monitoringEvent => monitoringEvent.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(monitoringEvent => monitoringEvent.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
