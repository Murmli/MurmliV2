using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Identity;

internal sealed class DeletedUserConfiguration : IEntityTypeConfiguration<DeletedUser>
{
    public void Configure(EntityTypeBuilder<DeletedUser> builder)
    {
        builder.ToTable("deleted_users", IdentitySchema.Name);
        builder.HasKey(deletedUser => deletedUser.Id).HasName("pk_deleted_users");
        builder.HasIndex(deletedUser => deletedUser.AuthUserId).HasDatabaseName("ix_deleted_users_auth_user_id");

        builder.Property(deletedUser => deletedUser.Id).HasColumnName("id");
        builder.Property(deletedUser => deletedUser.AuthUserId).HasColumnName("auth_user_id");
        builder.Property(deletedUser => deletedUser.DeletedAt).HasColumnName("deleted_at");
    }
}
