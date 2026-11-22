using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Data.Sync;

internal sealed class SyncMemberEntryConfiguration : IEntityTypeConfiguration<SyncMemberEntry>
{
    public void Configure(EntityTypeBuilder<SyncMemberEntry> builder)
    {
        builder.ToTable("member_entries", SyncSchema.Name);
        builder.HasKey(entry => entry.Id).HasName("pk_member_entries");
        builder
            .HasIndex(entry => new { entry.ScopeId, entry.AccountId })
            .IsUnique()
            .HasDatabaseName("ux_member_entries_scope_id_account_id");
        builder.HasIndex(entry => entry.AccountId).HasDatabaseName("ix_member_entries_account_id");

        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.ScopeId).HasColumnName("scope_id");
        builder.Property(entry => entry.AccountId).HasColumnName("account_id");
        builder.Property(entry => entry.Name).HasColumnName("name");
        builder.Property(entry => entry.Role).HasColumnName("role");
        builder
            .Property(entry => entry.State)
            .HasColumnName("state")
            .HasConversion(state => state.Key, key => SyncMemberState.Get(key));
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at");

        builder
            .HasOne<SyncScope>()
            .WithMany()
            .HasForeignKey(entry => entry.ScopeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_member_entries_scopes_scope_id");
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(entry => entry.AccountId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_member_entries_users_account_id");
    }
}
