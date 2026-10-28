using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class FirewallRuleConfiguration : IEntityTypeConfiguration<FirewallRule>
{
    public void Configure(EntityTypeBuilder<FirewallRule> builder)
    {
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.DeviceId);
        builder.Property(rule => rule.Name).HasMaxLength(FirewallRule.MaxNameLength).IsRequired();
        builder.Property(rule => rule.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Protocol).HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Ports).HasMaxLength(FirewallRule.MaxPortsLength);
        builder.Property(rule => rule.RemoteAddresses).HasMaxLength(FirewallRule.MaxRemoteAddressesLength);
        builder.Property(rule => rule.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.CreatedAt);
        builder.Property(rule => rule.CreatedByUserName).HasMaxLength(User.MaxUserNameLength).IsRequired();
        builder.Property(rule => rule.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.StateMessage).HasMaxLength(FirewallRule.MaxStateMessageLength);
        builder.Property(rule => rule.ReportedAt);

        // The rules are the desired state of the device; without the device they mean nothing.
        builder.HasOne<Device>().WithMany().HasForeignKey(rule => rule.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(rule => rule.DeviceId);
    }
}
