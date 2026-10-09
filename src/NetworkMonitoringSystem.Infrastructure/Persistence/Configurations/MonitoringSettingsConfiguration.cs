using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class MonitoringSettingsConfiguration : IEntityTypeConfiguration<MonitoringSettings>
{
    public void Configure(EntityTypeBuilder<MonitoringSettings> builder)
    {
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        // The single settings row always exists, with default values until an administrator changes them.
        builder.HasData(new MonitoringSettings());
    }
}
