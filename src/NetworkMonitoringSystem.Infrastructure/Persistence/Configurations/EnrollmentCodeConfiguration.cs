using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class EnrollmentCodeConfiguration : IEntityTypeConfiguration<EnrollmentCode>
{
    public void Configure(EntityTypeBuilder<EnrollmentCode> builder)
    {
        builder.HasKey(code => code.Id);

        builder.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(code => code.Note).HasMaxLength(EnrollmentCode.MaxNoteLength);
        builder.Property(code => code.DeviceId);
        builder.Property(code => code.CreatedAt);
        builder.Property(code => code.ExpiresAt);
        builder.Property(code => code.CreatedByUserName).HasMaxLength(User.MaxUserNameLength).IsRequired();
        builder.Property(code => code.UsedAt);
        builder.Property(code => code.UsedByHostName).HasMaxLength(255);
        builder.Property(code => code.RevokedAt);

        // Two agents registering with one code at the same moment: the second save fails on the changed row.
        builder.Property<uint>("xmin").IsRowVersion();

        // A code made for a device goes with it; turned into a code without a device it would admit any computer.
        builder.HasOne<Device>().WithMany().HasForeignKey(code => code.DeviceId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(code => code.CodeHash).IsUnique();
        builder.HasIndex(code => code.CreatedAt);
    }
}
