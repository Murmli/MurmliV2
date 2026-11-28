using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Sync;

internal sealed class DatabaseIdentityConfiguration : IEntityTypeConfiguration<DatabaseIdentity>
{
    public void Configure(EntityTypeBuilder<DatabaseIdentity> builder)
    {
        builder.ToTable(
            "database_identity",
            SyncSchema.Name,
            table => table.HasCheckConstraint("ck_database_identity_singleton", "singleton")
        );
        builder.HasKey(identity => identity.Singleton).HasName("pk_database_identity");

        builder.Property(identity => identity.Singleton).HasColumnName("singleton").ValueGeneratedNever();
        builder.Property(identity => identity.Value).HasColumnName("value");

        builder.HasData(new DatabaseIdentity { Singleton = true, Value = new Guid("3643d4ef-a971-42c0-be19-cc2c3180dc55") });
    }
}
