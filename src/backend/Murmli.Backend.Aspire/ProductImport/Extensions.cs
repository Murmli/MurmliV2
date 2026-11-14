using Aspire.Hosting.Azure;

namespace Murmli.Backend.Aspire.ProductImport;

public static class Extensions
{
    public static IResourceBuilder<ProjectResource> AddProductImport(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> database,
        IResourceBuilder<AzureUserAssignedIdentityResource> identity
    ) =>
        builder
            .AddProject<Projects.Murmli_Backend_ProductImport>("product-import")
            .WithReference(database)
            .WaitFor(database)
            .WithAzureUserAssignedIdentity(identity)
            // Keeps aspire run and dotnet test from downloading the export
            .WithExplicitStart()
            // Cron is UTC, Open Food Facts uploads the export around 05:30 UTC
            .PublishAsScheduledAzureContainerAppJob(
                "0 8 * * *",
                (_, job) =>
                {
                    // Aspire's default of 1800 s also covers the retries and is too short for the 13.6 GB export
                    job.Configuration.ReplicaTimeout = 7200;
                    job.Configuration.ReplicaRetryLimit = 1;
                    var container = job.Template.Containers.Single().Value!;
                    container.Resources.Cpu = 0.5;
                    container.Resources.Memory = "1Gi";
                }
            );
}
