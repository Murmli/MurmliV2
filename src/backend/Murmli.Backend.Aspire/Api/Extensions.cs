using Aspire.Hosting.Azure;

namespace Murmli.Backend.Aspire.Api;

public static class Extensions
{
    public static IResourceBuilder<ProjectResource> AddApi(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> database
    ) =>
        builder
            .AddProject<Projects.Murmli_Backend_Api>("api")
            .WithReference(database)
            .WaitFor(database)
            .WithExternalHttpEndpoints()
            .WithHttpHealthCheck("/health")
            .PublishAsAzureContainerApp(
                (_, containerApp) =>
                {
                    containerApp.Template.Scale.MinReplicas = 0;
                    containerApp.Template.Scale.MaxReplicas = 1;
                    var container = containerApp.Template.Containers.Single().Value!;
                    container.Resources.Cpu = 0.25;
                    container.Resources.Memory = "0.5Gi";
                }
            );
}
