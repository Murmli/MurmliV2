using Aspire.Hosting.Azure;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.PostgreSql;
using Azure.Provisioning.Primitives;

namespace Murmli.Backend.Aspire.Database;

public static class Extensions
{
    public static IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> AddDatabase(this IDistributedApplicationBuilder builder) =>
        builder
            .AddAzurePostgresFlexibleServer("postgres")
            .ConfigureInfrastructure(infrastructure =>
            {
                var resources = infrastructure.GetProvisionableResources().ToList();

                var server = resources.OfType<PostgreSqlFlexibleServer>().Single();
                // Azure.Provisioning.PostgreSql 1.1.1 knows neither version 18 nor API 2025-08-01, the first stable API supporting it
                server.ResourceVersion = "2025-08-01";
                server.Version = new StringLiteralExpression("18");

                var allowedExtensions = new PostgreSqlFlexibleServerConfiguration("postgresAllowedExtensions")
                {
                    Parent = server,
                    Name = "azure.extensions",
                    Value = "pg_trgm",
                    Source = "user-override",
                };
                // The server rejects concurrent writes of its child resources
                foreach (var childResource in resources.OfType<ProvisionableResource>().Where(resource => resource is PostgreSqlFlexibleServerDatabase or PostgreSqlFlexibleServerFirewallRule))
                {
                    allowedExtensions.DependsOn.Add(childResource);
                }

                infrastructure.Add(allowedExtensions);
            })
            .RunAsContainer(postgres => postgres.WithImageTag("18"))
            .AddDatabase("murmli");
}
