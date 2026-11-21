using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Data.Shopping;

internal sealed class ShoppingListMemberConfiguration : IEntityTypeConfiguration<ShoppingListMember>
{
    public void Configure(EntityTypeBuilder<ShoppingListMember> builder)
    {
        builder.ToTable("list_members", ShoppingSchema.Name);
        builder.HasKey(member => member.Id).HasName("pk_list_members");
        builder.HasIndex(member => new { member.ListId, member.UserId }).IsUnique().HasDatabaseName("ix_list_members_list_id_user_id");
        builder.HasIndex(member => member.UserId).HasDatabaseName("ix_list_members_user_id");
        builder
            .HasOne<ShoppingList>()
            .WithMany()
            .HasForeignKey(member => member.ListId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_list_members_lists_list_id");
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_list_members_users_user_id");

        builder.Property(member => member.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(member => member.ListId).HasColumnName("list_id");
        builder.Property(member => member.UserId).HasColumnName("user_id");
        builder.Property(member => member.CreatedAt).HasColumnName("created_at");
    }
}
