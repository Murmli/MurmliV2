using Aspire.Hosting.Azure;
using Aspire.Hosting.JavaScript;
using Microsoft.Extensions.Configuration;
using Murmli.Backend.Aspire.Auth;
using Murmli.Backend.Aspire.Telemetry;

namespace Murmli.Backend.Aspire.Api;

public static class Extensions
{
    public static IResourceBuilder<ProjectResource> AddApi(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> database,
        IResourceBuilder<AzureUserAssignedIdentityResource> identity,
        IResourceBuilder<AzureApplicationInsightsResource>? applicationInsights,
        IResourceBuilder<NodeAppResource> auth,
        IResourceBuilder<ParameterResource> authServiceKey
    ) =>
        builder
            .AddProject<Projects.Murmli_Backend_Api_Host>("api")
            .WithReference(database)
            .WaitFor(database)
            .WithAzureUserAssignedIdentity(identity)
            .WithTelemetry(applicationInsights)
            .WithAuth(auth, authServiceKey)
            .WithOpenApiDocument()
            .WithExternalHttpEndpoints()
            .WithHttpHealthCheck("/health")
            .WithCustomDomain()
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

    private static IResourceBuilder<ProjectResource> WithOpenApiDocument(this IResourceBuilder<ProjectResource> resource)
    {
        if (resource.ApplicationBuilder.Configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            resource.WithEnvironment("OpenApi__Enabled", "true");
        }

        return resource;
    }

    private static IResourceBuilder<ProjectResource> WithCustomDomain(this IResourceBuilder<ProjectResource> resource)
    {
        var builder = resource.ApplicationBuilder;

        // Publish only, aspire run would otherwise ask for values it never uses
        if (builder.ExecutionContext.IsPublishMode)
        {
            var customDomain = builder.AddParameter("api-custom-domain");
            // Aspire treats an empty configured value as missing, the value overload keeps it
            var certificateName = builder.AddParameter("api-certificate-name", builder.Configuration["Parameters:api-certificate-name"] ?? string.Empty);
            resource.PublishAsAzureContainerApp((_, containerApp) => containerApp.ConfigureCustomDomain(customDomain, certificateName));
        }

        return resource;
    }
}
