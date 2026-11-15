using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data.Identity;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.Data;

public class MurmliDbContext(DbContextOptions<MurmliDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImportRun> ProductImportRuns => Set<ProductImportRun>();

    public DbSet<User> Users => Set<User>();

    public DbSet<DeletedUser> DeletedUsers => Set<DeletedUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MurmliDbContext).Assembly);
    }
}
