using Aspire.Hosting.Azure;

namespace Murmli.Backend.Aspire.Identity;

public static class Extensions
{
    public static IResourceBuilder<AzureUserAssignedIdentityResource> AddBackendIdentity(this IDistributedApplicationBuilder builder) =>
        builder.AddAzureUserAssignedIdentity("backend-identity");
}
