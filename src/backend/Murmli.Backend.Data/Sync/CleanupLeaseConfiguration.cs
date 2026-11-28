using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Sync;

internal sealed class CleanupLeaseConfiguration : IEntityTypeConfiguration<CleanupLease>
{
    public void Configure(EntityTypeBuilder<CleanupLease> builder)
    {
        builder.ToTable("cleanup_lease", SyncSchema.Name, table => table.HasCheckConstraint("ck_cleanup_lease_singleton", "singleton"));
        builder.HasKey(lease => lease.Singleton).HasName("pk_cleanup_lease");

        builder.Property(lease => lease.Singleton).HasColumnName("singleton").ValueGeneratedNever();
        builder.Property(lease => lease.Holder).HasColumnName("holder");
        builder.Property(lease => lease.ExpiresAt).HasColumnName("expires_at");

        builder.HasData(new CleanupLease { Singleton = true });
    }
}
