using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Data.Sync;

internal sealed class ProcessedSyncCommandConfiguration : IEntityTypeConfiguration<ProcessedSyncCommand>
{
    public void Configure(EntityTypeBuilder<ProcessedSyncCommand> builder)
    {
        builder.ToTable("processed_commands", SyncSchema.Name);
        builder.HasKey(command => new { command.AccountId, command.CommandId }).HasName("pk_processed_commands");
        builder.HasIndex(command => command.RetainFrom).HasDatabaseName("ix_processed_commands_retain_from");
        builder
            .HasIndex(command => new { command.AccountId, command.ScopeId })
            .HasFilter("kind = 'delete' AND status = 'applied' AND record_id = scope_id")
            .HasDatabaseName("ix_processed_commands_root_deletes");

        builder.Property(command => command.AccountId).HasColumnName("account_id").ValueGeneratedNever();
        builder.Property(command => command.CommandId).HasColumnName("command_id").ValueGeneratedNever();
        builder.Property(command => command.Kind).HasColumnName("kind");
        builder.Property(command => command.ScopeId).HasColumnName("scope_id");
        builder.Property(command => command.DataType).HasColumnName("data_type");
        builder.Property(command => command.RecordId).HasColumnName("record_id");
        builder.Property(command => command.ProcessedAt).HasColumnName("processed_at");
        builder.Property(command => command.RetainFrom).HasColumnName("retain_from");
        builder
            .Property(command => command.Status)
            .HasColumnName("status")
            // EF never calls a converter for NULL
            .HasConversion(status => status!.Key, key => SyncResultStatus.Get(key));
        builder.Property(command => command.Reason).HasColumnName("reason");
        builder.Property(command => command.Cause).HasColumnName("cause");

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(command => command.AccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_processed_commands_users_account_id");
    }
}
