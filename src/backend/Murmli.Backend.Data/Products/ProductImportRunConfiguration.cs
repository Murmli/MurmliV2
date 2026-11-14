using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Murmli.Backend.Data.Products;

internal sealed class ProductImportRunConfiguration : IEntityTypeConfiguration<ProductImportRun>
{
    public void Configure(EntityTypeBuilder<ProductImportRun> builder)
    {
        builder.ToTable("product_import_runs", ProductsSchema.Name);
        builder.HasKey(run => run.Id).HasName("pk_product_import_runs");

        builder.Property(run => run.Id).HasColumnName("id");
        builder.Property(run => run.StartedAt).HasColumnName("started_at");
        builder.Property(run => run.FinishedAt).HasColumnName("finished_at");
        builder
            .Property(run => run.Status)
            .HasColumnName("status")
            .HasConversion(status => status.Key, key => ProductImportRunStatus.Get(key));
        builder.Property(run => run.PeakMemoryBytes).HasColumnName("peak_memory_bytes");
    }
}
