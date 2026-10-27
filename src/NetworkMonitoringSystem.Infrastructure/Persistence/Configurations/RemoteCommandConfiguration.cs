using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Commands;
using NetworkMonitoringSystem.Domain.Devices;

namespace NetworkMonitoringSystem.Infrastructure.Persistence.Configurations;

internal sealed class RemoteCommandConfiguration : IEntityTypeConfiguration<RemoteCommand>
{
    public void Configure(EntityTypeBuilder<RemoteCommand> builder)
    {
        builder.HasKey(command => command.Id);

        builder.Property(command => command.DeviceId);
        builder.Property(command => command.DeviceName).HasMaxLength(200).IsRequired();
        builder.Property(command => command.RequestedByUserId);
        builder.Property(command => command.RequestedByUserName).HasMaxLength(User.MaxUserNameLength).IsRequired();
        builder.Property(command => command.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(command => command.Parameter).HasMaxLength(RemoteCommand.MaxParameterLength);
        builder.Property(command => command.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(command => command.RequestedAt);
        builder.Property(command => command.SentAt);
        builder.Property(command => command.CompletedAt);
        builder.Property(command => command.ResultMessage).HasMaxLength(RemoteCommand.MaxResultLength);
        builder.Ignore(command => command.IsFinished);

        // The record is an audit log, so it outlives both the device and the user; it keeps their names.
        builder.HasOne<Device>().WithMany().HasForeignKey(command => command.DeviceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(command => command.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(command => new { command.DeviceId, command.RequestedAt });
    }
}

internal sealed class FileTransferConfiguration : IEntityTypeConfiguration<FileTransfer>
{
    public void Configure(EntityTypeBuilder<FileTransfer> builder)
    {
        builder.HasKey(transfer => transfer.CommandId);

        builder.Property(transfer => transfer.FileName).HasMaxLength(FileTransfer.MaxFileNameLength).IsRequired();
        builder.Property(transfer => transfer.Size);
        builder.Property(transfer => transfer.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(transfer => transfer.CreatedAt);
        builder.Ignore(transfer => transfer.ChunkCount);

        // Part of the audit record of its command, so it lives as long as the command does.
        builder.HasOne<RemoteCommand>().WithOne().HasForeignKey<FileTransfer>(transfer => transfer.CommandId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FileTransferChunkConfiguration : IEntityTypeConfiguration<FileTransferChunk>
{
    public void Configure(EntityTypeBuilder<FileTransferChunk> builder)
    {
        builder.HasKey(chunk => new { chunk.CommandId, chunk.Index });

        builder.Property(chunk => chunk.Data).IsRequired();

        builder.HasOne<FileTransfer>().WithMany().HasForeignKey(chunk => chunk.CommandId).OnDelete(DeleteBehavior.Cascade);
    }
}
