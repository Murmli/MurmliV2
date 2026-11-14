using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.Data;

public class MurmliDbContext(DbContextOptions<MurmliDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MurmliDbContext).Assembly);
    }
}
