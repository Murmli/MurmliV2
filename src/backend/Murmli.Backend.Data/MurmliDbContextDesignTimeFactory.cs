using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Murmli.Backend.Data;

// The Aspire registration requests an Azure token at design time, so tooling builds the context without it
internal sealed class MurmliDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MurmliDbContext>
{
    public MurmliDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MurmliDbContext>().UseNpgsql("Host=localhost;Database=murmli").Options);
}
