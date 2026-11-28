using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestCardConfiguration : IEntityTypeConfiguration<TestCard>
{
    public void Configure(EntityTypeBuilder<TestCard> builder)
    {
        builder.ToTable("cards", SyncTestSchema.Name, table => table.ExcludeFromMigrations());
        builder.HasKey(card => card.Id).HasName("pk_cards");
        builder.HasIndex(card => new { card.ScopeId, card.Version }).HasDatabaseName("ix_cards_scope_id_version");
        builder
            .HasIndex(card => card.ChangedByMemberId)
            .HasFilter("changed_by_member_id IS NOT NULL")
            .HasDatabaseName("ix_cards_changed_by_member_id");
        builder
            .HasIndex(card => card.CreatedByMemberId)
            .HasFilter("created_by_member_id IS NOT NULL")
            .HasDatabaseName("ix_cards_created_by_member_id");
        builder
            .HasIndex(card => card.DoneByMemberId)
            .HasFilter("done_by_member_id IS NOT NULL")
            .HasDatabaseName("ix_cards_done_by_member_id");

        builder.Property(card => card.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(card => card.ScopeId).HasColumnName("scope_id");
        builder.Property(card => card.Version).HasColumnName("version");
        builder.Property(card => card.Text).HasColumnName("text");
        builder.Property(card => card.TextChangedAt).HasColumnName("text_changed_at");
        builder.Property(card => card.Count).HasColumnName("count").HasPrecision(7, 3);
        builder.Property(card => card.Measure).HasColumnName("measure");
        builder.Property(card => card.AmountChangedAt).HasColumnName("amount_changed_at");
        builder.Property(card => card.Done).HasColumnName("done");
        builder.Property(card => card.DoneChangedAt).HasColumnName("done_changed_at");
        builder.Property(card => card.ChangedByMemberId).HasColumnName("changed_by_member_id");
        builder.Property(card => card.CreatedByMemberId).HasColumnName("created_by_member_id");
        builder.Property(card => card.DoneByMemberId).HasColumnName("done_by_member_id");
        builder.Property(card => card.CreatedAt).HasColumnName("created_at");

        builder
            .HasOne<SyncScope>()
            .WithMany()
            .HasForeignKey(card => card.ScopeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_cards_scopes_scope_id");
        builder
            .HasOne<SyncMemberEntry>()
            .WithMany()
            .HasForeignKey(card => card.ChangedByMemberId)
            .OnDelete(DeleteBehavior.ClientNoAction)
            .HasConstraintName("fk_cards_member_entries_changed_by_member_id");
        builder
            .HasOne<SyncMemberEntry>()
            .WithMany()
            .HasForeignKey(card => card.CreatedByMemberId)
            .OnDelete(DeleteBehavior.ClientNoAction)
            .HasConstraintName("fk_cards_member_entries_created_by_member_id");
        builder
            .HasOne<SyncMemberEntry>()
            .WithMany()
            .HasForeignKey(card => card.DoneByMemberId)
            .OnDelete(DeleteBehavior.ClientNoAction)
            .HasConstraintName("fk_cards_member_entries_done_by_member_id");
    }
}
