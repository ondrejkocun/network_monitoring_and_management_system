using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class ProcessRunConfiguration : IEntityTypeConfiguration<ProcessRun>
{
    public void Configure(EntityTypeBuilder<ProcessRun> builder)
    {
        builder.HasKey(run => run.Id);

        builder.Property(run => run.DeviceId);
        builder.Property(run => run.Pid);
        builder.Property(run => run.Name).HasMaxLength(ProcessRun.MaxNameLength).IsRequired();
        builder.Property(run => run.StartedAt);
        builder.Property(run => run.EndedAt);
        builder.Ignore(run => run.IsRunning);

        builder.HasOne<Device>().WithMany().HasForeignKey(run => run.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(run => new { run.DeviceId, run.EndedAt });
    }
}

internal sealed class ProcessUsageConfiguration : IEntityTypeConfiguration<ProcessUsage>
{
    public void Configure(EntityTypeBuilder<ProcessUsage> builder)
    {
        builder.ToTable("ProcessUsages");
        builder.HasKey(usage => usage.Id);

        builder.Property(usage => usage.CpuUsagePercent);
        builder.Property(usage => usage.MemoryBytes);

        builder.HasOne(usage => usage.ProcessRun).WithMany().HasForeignKey("ProcessRunId").IsRequired().OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ListeningPortConfiguration : IEntityTypeConfiguration<ListeningPort>
{
    public void Configure(EntityTypeBuilder<ListeningPort> builder)
    {
        builder.HasKey(port => port.Id);

        builder.Property(port => port.DeviceId);
        builder.Property(port => port.Protocol).HasConversion<string>().HasMaxLength(10);
        builder.Property(port => port.LocalAddress).HasMaxLength(ListeningPort.MaxAddressLength).IsRequired();
        builder.Property(port => port.Port);
        builder.Property(port => port.OpenedAt);
        builder.Property(port => port.ClosedAt);
        builder.Ignore(port => port.IsOpen);

        builder.HasOne<Device>().WithMany().HasForeignKey(port => port.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(port => port.ProcessRun).WithMany().HasForeignKey("ProcessRunId").OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(port => new { port.DeviceId, port.ClosedAt });
    }
}

internal sealed class ActiveConnectionConfiguration : IEntityTypeConfiguration<ActiveConnection>
{
    public void Configure(EntityTypeBuilder<ActiveConnection> builder)
    {
        builder.HasKey(connection => connection.Id);

        builder.Property(connection => connection.DeviceId);
        builder.Property(connection => connection.Protocol).HasConversion<string>().HasMaxLength(10);
        builder.Property(connection => connection.LocalAddress).HasMaxLength(ListeningPort.MaxAddressLength).IsRequired();
        builder.Property(connection => connection.LocalPort);
        builder.Property(connection => connection.RemoteAddress).HasMaxLength(ListeningPort.MaxAddressLength).IsRequired();
        builder.Property(connection => connection.RemotePort);
        builder.Property(connection => connection.State).HasMaxLength(ActiveConnection.MaxStateLength).IsRequired();

        builder.HasOne<Device>().WithMany().HasForeignKey(connection => connection.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(connection => connection.ProcessRun).WithMany().HasForeignKey("ProcessRunId").OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(connection => connection.DeviceId);
    }
}
