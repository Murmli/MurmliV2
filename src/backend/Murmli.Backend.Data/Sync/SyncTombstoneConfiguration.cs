using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Sync;

internal sealed class SyncTombstoneConfiguration : IEntityTypeConfiguration<SyncTombstone>
{
    public void Configure(EntityTypeBuilder<SyncTombstone> builder)
    {
        builder.ToTable("tombstones", SyncSchema.Name);
        builder.HasKey(tombstone => new { tombstone.DataType, tombstone.RecordId }).HasName("pk_tombstones");
        builder.HasIndex(tombstone => new { tombstone.ScopeId, tombstone.Version }).HasDatabaseName("ix_tombstones_scope_id_version");
        builder.HasIndex(tombstone => tombstone.DeletedAt).HasDatabaseName("ix_tombstones_deleted_at");

        builder.Property(tombstone => tombstone.DataType).HasColumnName("data_type");
        builder.Property(tombstone => tombstone.RecordId).HasColumnName("record_id").ValueGeneratedNever();
        builder.Property(tombstone => tombstone.ScopeId).HasColumnName("scope_id");
        builder.Property(tombstone => tombstone.Version).HasColumnName("version");
        builder.Property(tombstone => tombstone.DeletedAt).HasColumnName("deleted_at");

        builder
            .HasOne<SyncScope>()
            .WithMany()
            .HasForeignKey(tombstone => tombstone.ScopeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_tombstones_scopes_scope_id");
    }
}
