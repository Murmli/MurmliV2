using Aspire.Hosting.Azure.AppContainers;

namespace Murmli.Backend.Aspire.ContainerAppEnvironment;

public static class Extensions
{
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddContainerAppEnvironment(this IDistributedApplicationBuilder builder) =>
        builder.AddAzureContainerAppEnvironment("env").WithDashboard(false);
}
