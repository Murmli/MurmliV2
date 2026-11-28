using Aspire.Hosting.Azure;
using Aspire.Hosting.JavaScript;
using Azure.Provisioning.AppContainers;

namespace Murmli.Backend.Aspire.CustomDomain;

public static class Extensions
{
    public static IResourceBuilder<ProjectResource> WithCustomDomain(this IResourceBuilder<ProjectResource> resource) =>
        AddCustomDomainConfiguration(resource.ApplicationBuilder, resource.Resource.Name) is { } configureCustomDomain
            ? resource.PublishAsAzureContainerApp(configureCustomDomain)
            : resource;

    public static IResourceBuilder<NodeAppResource> WithCustomDomain(this IResourceBuilder<NodeAppResource> resource) =>
        AddCustomDomainConfiguration(resource.ApplicationBuilder, resource.Resource.Name) is { } configureCustomDomain
            ? resource.PublishAsAzureContainerApp(configureCustomDomain)
            : resource;

    private static Action<AzureResourceInfrastructure, ContainerApp>? AddCustomDomainConfiguration(IDistributedApplicationBuilder builder, string resourceName)
    {
        // Publish only, aspire run would otherwise ask for values it never uses
        if (!builder.ExecutionContext.IsPublishMode)
        {
            return null;
        }

        var customDomain = builder.AddParameter($"{resourceName}-custom-domain");
        // Aspire treats an empty configured value as missing, the value overload keeps it
        var certificateName = builder.AddParameter($"{resourceName}-certificate-name", builder.Configuration[$"Parameters:{resourceName}-certificate-name"] ?? string.Empty);
        return (_, containerApp) => containerApp.ConfigureCustomDomain(customDomain, certificateName);
    }
}
