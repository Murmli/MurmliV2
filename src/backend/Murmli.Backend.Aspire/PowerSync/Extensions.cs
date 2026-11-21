using Aspire.Hosting.Azure;
using Aspire.Hosting.JavaScript;

namespace Murmli.Backend.Aspire.PowerSync;

public static class Extensions
{
    public static void AddPowerSync(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> database,
        IResourceBuilder<NodeAppResource> auth
    )
    {
        if (!builder.ExecutionContext.IsRunMode)
        {
            return;
        }

        var storage = builder.CreateResourceBuilder(database.Resource.Parent).AddDatabase("powersync-storage", "powersync_storage");

        builder
            .AddContainer("powersync", "journeyapps/powersync-service", "1.29.1")
            .WithArgs("start", "-r", "unified")
            .WithBindMount(Path.Combine(builder.AppHostDirectory, "PowerSync", "config"), "/config", isReadOnly: true)
            .WithEnvironment("POWERSYNC_CONFIG_PATH", "/config/service.yaml")
            .WithEnvironment("PS_DATA_SOURCE_URI", database.Resource.UriExpression)
            .WithEnvironment("PS_STORAGE_URI", storage.Resource.UriExpression)
            .WithEnvironment("PS_JWKS_URL", ReferenceExpression.Create($"{auth.GetEndpoint("http")}/api/auth/jwks"))
            .WithHttpEndpoint(port: 5022, targetPort: 8080)
            .WithExternalHttpEndpoints()
            .WaitFor(database)
            .WaitFor(storage)
            .WaitFor(auth);
    }
}
