using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning.ContainerRegistry;

namespace Murmli.Backend.Aspire.ContainerAppEnvironment;

public static class Extensions
{
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddContainerAppEnvironment(this IDistributedApplicationBuilder builder)
    {
        var environment = builder.AddAzureContainerAppEnvironment("env").WithDashboard(false);

        // Aspire has no public accessor for the default registry and adds it to the model only in publish mode
        var registry = builder.Resources.OfType<AzureContainerRegistryResource>().SingleOrDefault();
        if (builder.Configuration["ContainerRegistry:Sku"] is { } sku && registry is not null)
        {
            var skuName = Enum.Parse<ContainerRegistrySkuName>(sku, ignoreCase: true);
            builder
                .CreateResourceBuilder(registry)
                .ConfigureInfrastructure(infrastructure =>
                    infrastructure.GetProvisionableResources().OfType<ContainerRegistryService>().Single().Sku = new() { Name = skuName }
                );
        }

        return environment;
    }
}
