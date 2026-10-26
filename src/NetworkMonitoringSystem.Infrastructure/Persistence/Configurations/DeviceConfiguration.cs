using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(device => device.Id);
        builder.Property(device => device.Id).ValueGeneratedNever();

        builder.Property(device => device.Name).HasMaxLength(200).IsRequired();
        builder.Property(device => device.HostName).HasMaxLength(255);
        builder.Property(device => device.IpAddress);
        builder.Property(device => device.MonitoringMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(device => device.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(device => device.CreatedAt);
        builder.Property(device => device.LastSeenAt);
        builder.Property(device => device.IsEnabled);

        builder.Property(device => device.AgentKeyHash).HasMaxLength(64);
        builder.Property(device => device.OperatingSystem).HasMaxLength(200);
        builder.Property(device => device.TcpPort);
        builder.Property(device => device.CaptureTraffic);
        builder.Property(device => device.TrafficCaptureProblem).HasMaxLength(Device.MaxTrafficCaptureProblemLength);

        // PostgreSQL's system column xmin changes with every update of the row. An update made from an older copy
        // of the device therefore fails instead of silently overwriting a newer state.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(device => device.Name);
        builder.HasIndex(device => device.HostName);
    }
}
