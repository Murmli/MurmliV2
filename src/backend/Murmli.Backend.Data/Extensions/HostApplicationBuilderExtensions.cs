using Aspire.Azure.Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Telemetry.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Murmli.Backend.Data.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddMurmliDbContext(
        this IHostApplicationBuilder builder,
        Action<AzureNpgsqlEntityFrameworkCorePostgreSQLSettings>? configureSettings = null,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null
    ) =>
        builder.AddAzureNpgsqlDbContext<MurmliDbContext>(
            "murmli",
            configureSettings,
            configureDbContextOptions: options =>
            {
                options.EnableSensitiveDataLogging(builder.Configuration.RecordsSensitiveData());
                if (configureNpgsql is not null)
                {
                    // Runs after Aspire's UseNpgsql, so a retry strategy configured here replaces Aspire's default
                    options.UseNpgsql(configureNpgsql);
                }
            }
        );
}
