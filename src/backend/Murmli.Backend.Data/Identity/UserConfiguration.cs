using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", IdentitySchema.Name);
        builder.HasKey(user => user.Id).HasName("pk_users");
        builder.HasIndex(user => user.AuthUserId).IsUnique().HasDatabaseName("ix_users_auth_user_id");

        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(user => user.AuthUserId).HasColumnName("auth_user_id");
        builder.Property(user => user.CreatedAt).HasColumnName("created_at");
        builder.Property(user => user.DeletionStartedAt).HasColumnName("deletion_started_at");
        builder.Property(user => user.LastRequestOn).HasColumnName("last_request_on");
    }
}
