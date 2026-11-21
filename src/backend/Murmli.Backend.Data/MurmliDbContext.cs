using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data.Identity;
using Murmli.Backend.Data.Products;
using Murmli.Backend.Data.Shopping;

namespace Murmli.Backend.Data;

public class MurmliDbContext(DbContextOptions<MurmliDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImportRun> ProductImportRuns => Set<ProductImportRun>();

    public DbSet<User> Users => Set<User>();

    public DbSet<DeletedUser> DeletedUsers => Set<DeletedUser>();

    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();

    public DbSet<ShoppingListMember> ShoppingListMembers => Set<ShoppingListMember>();

    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MurmliDbContext).Assembly);
    }
}
