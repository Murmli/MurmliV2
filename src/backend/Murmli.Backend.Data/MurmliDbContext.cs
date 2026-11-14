using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.Data;

public class MurmliDbContext(DbContextOptions<MurmliDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImportRun> ProductImportRuns => Set<ProductImportRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MurmliDbContext).Assembly);
}
