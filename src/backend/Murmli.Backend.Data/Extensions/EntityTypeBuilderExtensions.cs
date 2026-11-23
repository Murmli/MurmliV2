using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Data.Extensions;

internal static class EntityTypeBuilderExtensions
{
    public static void MapSyncRecord<TRecord>(this EntityTypeBuilder<TRecord> builder, string table)
        where TRecord : SyncRecord
    {
        builder.HasKey(record => record.Id).HasName($"pk_{table}");
        builder.HasIndex(record => new { record.ScopeId, record.Version }).HasDatabaseName($"ix_{table}_scope_id_version");

        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(record => record.ScopeId).HasColumnName("scope_id");
        builder.Property(record => record.Version).HasColumnName("version");

        builder
            .HasOne<SyncScope>()
            .WithMany()
            .HasForeignKey(record => record.ScopeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName($"fk_{table}_scopes_scope_id");
    }
}
