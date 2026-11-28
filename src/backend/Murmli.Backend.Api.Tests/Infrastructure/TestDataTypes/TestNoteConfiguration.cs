using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestNoteConfiguration : IEntityTypeConfiguration<TestNote>
{
    public void Configure(EntityTypeBuilder<TestNote> builder)
    {
        builder.ToTable("notes", SyncTestSchema.Name, table => table.ExcludeFromMigrations());
        builder.HasKey(note => note.Id).HasName("pk_notes");
        builder.HasIndex(note => new { note.ScopeId, note.Version }).HasDatabaseName("ix_notes_scope_id_version");

        builder.Property(note => note.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(note => note.ScopeId).HasColumnName("scope_id");
        builder.Property(note => note.Version).HasColumnName("version");
        builder.Property(note => note.Title).HasColumnName("title");
        builder.Property(note => note.TitleChangedAt).HasColumnName("title_changed_at");
        builder.Property(note => note.Color).HasColumnName("color");
        builder.Property(note => note.Weight).HasColumnName("weight").HasPrecision(4, 1);
        builder.Property(note => note.StyleChangedAt).HasColumnName("style_changed_at");
        builder.Property(note => note.Pinned).HasColumnName("pinned");

        builder
            .HasOne<SyncScope>()
            .WithMany()
            .HasForeignKey(note => note.ScopeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_notes_scopes_scope_id");
    }
}
