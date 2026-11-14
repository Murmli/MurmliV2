using Aspire.Azure.Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.Extensions.Hosting;

namespace Murmli.Backend.Data;

public static class HostApplicationBuilderExtensions
{
    public static void AddMurmliDbContext(
        this IHostApplicationBuilder builder,
        Action<AzureNpgsqlEntityFrameworkCorePostgreSQLSettings>? configureSettings = null
    ) => builder.AddAzureNpgsqlDbContext<MurmliDbContext>("murmli", configureSettings);
}
