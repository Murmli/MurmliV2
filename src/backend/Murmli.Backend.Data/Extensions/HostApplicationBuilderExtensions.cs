using Aspire.Azure.Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Telemetry.Extensions;

namespace Murmli.Backend.Data.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddMurmliDbContext(
        this IHostApplicationBuilder builder,
        Action<AzureNpgsqlEntityFrameworkCorePostgreSQLSettings>? configureSettings = null
    ) =>
        builder.AddAzureNpgsqlDbContext<MurmliDbContext>(
            "murmli",
            configureSettings,
            configureDbContextOptions: options => options.EnableSensitiveDataLogging(builder.Configuration.RecordsSensitiveData())
        );
}
