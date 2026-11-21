using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Data.Shopping;

internal sealed class ProcessedCommandConfiguration : IEntityTypeConfiguration<ProcessedCommand>
{
    public void Configure(EntityTypeBuilder<ProcessedCommand> builder)
    {
        builder.ToTable("processed_commands", ShoppingSchema.Name);
        builder.HasKey(command => command.Id).HasName("pk_processed_commands");
        builder.HasIndex(command => command.UserId).HasDatabaseName("ix_processed_commands_user_id");
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(command => command.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_processed_commands_users_user_id");

        builder.Property(command => command.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(command => command.UserId).HasColumnName("user_id");
        builder.Property(command => command.ProcessedAt).HasColumnName("processed_at");
    }
}
