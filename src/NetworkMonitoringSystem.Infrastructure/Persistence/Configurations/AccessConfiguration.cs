using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(user => user.Id);

        builder.Property(user => user.UserName).HasMaxLength(User.MaxUserNameLength).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(user => user.IsActive);
        builder.Property(user => user.CreatedAt);
        builder.Property(user => user.FailedLoginCount);
        builder.Property(user => user.LockedUntil);

        builder.HasIndex(user => user.UserName).IsUnique();

        builder.HasMany(user => user.Roles).WithMany().UsingEntity<Dictionary<string, object>>(
            "UserRoles",
            join => join.HasOne<Role>().WithMany().HasForeignKey("RoleId").OnDelete(DeleteBehavior.Cascade),
            join => join.HasOne<User>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade));
        builder.Navigation(user => user.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Name).HasMaxLength(Role.MaxNameLength).IsRequired();
        builder.HasIndex(role => role.Name).IsUnique();

        builder.HasMany(role => role.Rules).WithOne().HasForeignKey("RoleId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(role => role.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AccessRuleConfiguration : IEntityTypeConfiguration<AccessRule>
{
    public void Configure(EntityTypeBuilder<AccessRule> builder)
    {
        builder.ToTable("AccessRules");
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Permission).HasConversion<string>().HasMaxLength(50);
        builder.Property(rule => rule.DeviceId);

        // A rule for one device has no meaning once the device is removed.
        builder.HasOne<Device>().WithMany().HasForeignKey(rule => rule.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.HasKey(session => session.Id);

        builder.Property(session => session.UserId);
        builder.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(session => session.CreatedAt);
        builder.Property(session => session.ExpiresAt);

        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
