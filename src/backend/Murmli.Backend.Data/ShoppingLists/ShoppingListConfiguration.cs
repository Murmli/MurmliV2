using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Data.ShoppingLists;

internal sealed class ShoppingListConfiguration : IEntityTypeConfiguration<ShoppingList>
{
    public void Configure(EntityTypeBuilder<ShoppingList> builder)
    {
        builder.ToTable("lists", ShoppingListsSchema.Name, table => table.HasCheckConstraint("ck_lists_scope_id", "scope_id = id"));
        builder.MapSyncRecord("lists");

        builder.Property(list => list.Name).HasColumnName("name");
        builder.Property(list => list.NameChangedAt).HasColumnName("name_changed_at");
    }
}
