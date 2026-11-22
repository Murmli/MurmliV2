using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestBoardConfiguration : IEntityTypeConfiguration<TestBoard>
{
    public void Configure(EntityTypeBuilder<TestBoard> builder)
    {
        builder.ToTable(
            "boards",
            SyncTestSchema.Name,
            table =>
            {
                table.ExcludeFromMigrations();
                table.HasCheckConstraint("ck_boards_scope_id", "scope_id = id");
            }
        );
        builder.HasKey(board => board.Id).HasName("pk_boards");
        builder.HasIndex(board => new { board.ScopeId, board.Version }).HasDatabaseName("ix_boards_scope_id_version");

        builder.Property(board => board.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(board => board.ScopeId).HasColumnName("scope_id");
        builder.Property(board => board.Version).HasColumnName("version");
        builder.Property(board => board.Title).HasColumnName("title");
        builder.Property(board => board.TitleChangedAt).HasColumnName("title_changed_at");

        builder
            .HasOne<SyncScope>()
            .WithMany()
            .HasForeignKey(board => board.ScopeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_boards_scopes_scope_id");
    }
}
