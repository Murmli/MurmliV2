using Aspire.Hosting.Azure;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.PostgreSql;

namespace Murmli.Backend.Aspire.Database;

public static class Extensions
{
    public static IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> AddDatabase(this IDistributedApplicationBuilder builder) =>
        builder
            .AddAzurePostgresFlexibleServer("postgres")
            .ConfigureInfrastructure(infrastructure =>
            {
                var server = infrastructure.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();
                // Azure.Provisioning.PostgreSql 1.1.1 knows neither version 18 nor API 2025-08-01, the first stable API supporting it
                server.ResourceVersion = "2025-08-01";
                server.Version = new StringLiteralExpression("18");
            })
            .RunAsContainer(postgres => postgres.WithImageTag("18"))
            .AddDatabase("murmli");
}
