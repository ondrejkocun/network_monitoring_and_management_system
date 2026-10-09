using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

        builder.HasOne<Device>().WithMany().HasForeignKey(snapshot => snapshot.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(snapshot => new { snapshot.DeviceId, snapshot.RecordedAt });
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
    }
}
