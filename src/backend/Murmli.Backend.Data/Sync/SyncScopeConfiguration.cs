using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Data.Sync;

internal sealed class SyncScopeConfiguration : IEntityTypeConfiguration<SyncScope>
{
    public void Configure(EntityTypeBuilder<SyncScope> builder)
    {
        builder.ToTable("scopes", SyncSchema.Name);
        builder.HasKey(scope => scope.Id).HasName("pk_scopes");
        builder.HasIndex(scope => new { scope.OwnerAccountId, scope.Kind }).HasDatabaseName("ix_scopes_owner_account_id_kind");
        builder
            .HasIndex(scope => scope.OwnerAccountId)
            .IsUnique()
            .HasFilter("kind = 'personal'")
            .HasDatabaseName("ux_scopes_owner_account_id_personal");

        builder.Property(scope => scope.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(scope => scope.Kind).HasColumnName("kind");
        builder.Property(scope => scope.OwnerAccountId).HasColumnName("owner_account_id");
        builder.Property(scope => scope.Version).HasColumnName("version");
        builder.Property(scope => scope.Epoch).HasColumnName("epoch");
        builder.Property(scope => scope.TombstoneFloor).HasColumnName("tombstone_floor");
        builder.Property(scope => scope.CreatedAt).HasColumnName("created_at");

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(scope => scope.OwnerAccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_scopes_users_owner_account_id");
    }
}
