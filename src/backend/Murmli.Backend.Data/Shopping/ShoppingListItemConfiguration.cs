using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Shopping;

internal sealed class ShoppingListItemConfiguration : IEntityTypeConfiguration<ShoppingListItem>
{
    public void Configure(EntityTypeBuilder<ShoppingListItem> builder)
    {
        builder.ToTable("items", ShoppingSchema.Name);
        builder.HasKey(item => item.Id).HasName("pk_items");
        builder.HasIndex(item => new { item.ListId, item.Version }).HasDatabaseName("ix_items_list_id_version");
        builder
            .HasOne<ShoppingList>()
            .WithMany()
            .HasForeignKey(item => item.ListId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_items_lists_list_id");

        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.ListId).HasColumnName("list_id");
        builder.Property(item => item.Name).HasColumnName("name");
        builder.Property(item => item.NameChangedAt).HasColumnName("name_changed_at");
        builder.Property(item => item.Quantity).HasColumnName("quantity");
        builder.Property(item => item.Unit).HasColumnName("unit");
        builder.Property(item => item.Category).HasColumnName("category");
        builder.Property(item => item.Checked).HasColumnName("checked");
        builder.Property(item => item.CheckedChangedAt).HasColumnName("checked_changed_at");
        builder.Property(item => item.Version).HasColumnName("version");
        builder.Property(item => item.DeletedAt).HasColumnName("deleted_at");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at");
    }
}
