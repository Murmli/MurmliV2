using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Data.ShoppingLists;

internal sealed class ShoppingListItemConfiguration : IEntityTypeConfiguration<ShoppingListItem>
{
    public void Configure(EntityTypeBuilder<ShoppingListItem> builder)
    {
        builder.ToTable("items", ShoppingListsSchema.Name);
        builder.MapSyncRecord("items");
        builder
            .HasIndex(item => item.CheckedArrivedAt)
            .HasFilter("checked_arrived_at IS NOT NULL")
            .HasDatabaseName("ix_items_checked_arrived_at");
        builder
            .HasIndex(item => item.ChangedByMemberId)
            .HasFilter("changed_by_member_id IS NOT NULL")
            .HasDatabaseName("ix_items_changed_by_member_id");
        builder
            .HasIndex(item => item.CreatedByMemberId)
            .HasFilter("created_by_member_id IS NOT NULL")
            .HasDatabaseName("ix_items_created_by_member_id");

        builder.Property(item => item.Name).HasColumnName("name");
        builder.Property(item => item.NameChangedAt).HasColumnName("name_changed_at");
        builder.Property(item => item.Quantity).HasColumnName("quantity").HasPrecision(12, 3);
        builder.Property(item => item.Unit).HasColumnName("unit");
        builder.Property(item => item.AmountChangedAt).HasColumnName("amount_changed_at");
        builder.Property(item => item.Category).HasColumnName("category");
        builder.Property(item => item.CategoryChangedAt).HasColumnName("category_changed_at");
        builder.Property(item => item.Checked).HasColumnName("checked");
        builder.Property(item => item.CheckedChangedAt).HasColumnName("checked_changed_at");
        builder.Property(item => item.Language).HasColumnName("language");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");
        builder.Property(item => item.ChangedAt).HasColumnName("changed_at");
        builder.Property(item => item.ChangedByMemberId).HasColumnName("changed_by_member_id");
        builder.Property(item => item.CreatedByMemberId).HasColumnName("created_by_member_id");
        builder.Property(item => item.CheckedArrivedAt).HasColumnName("checked_arrived_at");

        builder
            .HasOne<SyncMemberEntry>()
            .WithMany()
            .HasForeignKey(item => item.ChangedByMemberId)
            .OnDelete(DeleteBehavior.ClientNoAction)
            .HasConstraintName("fk_items_member_entries_changed_by_member_id");
        builder
            .HasOne<SyncMemberEntry>()
            .WithMany()
            .HasForeignKey(item => item.CreatedByMemberId)
            .OnDelete(DeleteBehavior.ClientNoAction)
            .HasConstraintName("fk_items_member_entries_created_by_member_id");
    }
}
