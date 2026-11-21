using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Shopping;

internal sealed class ShoppingListConfiguration : IEntityTypeConfiguration<ShoppingList>
{
    public void Configure(EntityTypeBuilder<ShoppingList> builder)
    {
        builder.ToTable("lists", ShoppingSchema.Name);
        builder.HasKey(list => list.Id).HasName("pk_lists");

        builder.Property(list => list.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(list => list.Name).HasColumnName("name");
        builder.Property(list => list.NameChangedAt).HasColumnName("name_changed_at");
        builder.Property(list => list.Version).HasColumnName("version").HasDefaultValue(0L);
        builder.Property(list => list.CreatedAt).HasColumnName("created_at");
        builder.Property(list => list.UpdatedAt).HasColumnName("updated_at");
    }
}
