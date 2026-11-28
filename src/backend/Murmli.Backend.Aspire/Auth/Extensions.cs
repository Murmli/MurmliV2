using Aspire.Hosting.Azure;
using Aspire.Hosting.JavaScript;
using Murmli.Backend.Aspire.CustomDomain;
using Murmli.Backend.Aspire.Telemetry;

namespace Murmli.Backend.Aspire.Auth;

public static class Extensions
{
    private const string JwtAudience = "murmli-api";

    public static IResourceBuilder<ParameterResource> AddBetterAuthSecret(this IDistributedApplicationBuilder builder) =>
        builder.AddParameter("better-auth-secret", secret: true);

    public static IResourceBuilder<ParameterResource> AddAuthServiceKey(this IDistributedApplicationBuilder builder) =>
        builder.AddParameter("auth-service-key", secret: true);

    public static IResourceBuilder<NodeAppResource> AddAuth(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> database,
        IResourceBuilder<AzureUserAssignedIdentityResource> identity,
        IResourceBuilder<AzureApplicationInsightsResource>? applicationInsights,
        IResourceBuilder<ParameterResource> betterAuthSecret,
        IResourceBuilder<ParameterResource> serviceKey
    )
    {
        var appleBundleIdentifier = RequiredConfigurationValue(builder, "Apple:BundleIdentifier");
        var googleWebClientId = RequiredConfigurationValue(builder, "Google:WebClientId");

        var auth = builder
            .AddNodeApp("auth", "../../auth", "src/main.ts")
            // Fixed locally so local clients (Bruno, the app's local flavor) have a stable address
            .WithHttpEndpoint(port: builder.ExecutionContext.IsRunMode ? 5021 : null, env: "PORT")
            .WithExternalHttpEndpoints()
            .WithHttpHealthCheck("/health")
            .WithCustomDomain()
            .WithReference(database)
            .WaitFor(database)
            .WithAzureUserAssignedIdentity(identity)
            .WithTelemetry(applicationInsights)
            .WithEnvironment("BETTER_AUTH_SECRET", betterAuthSecret)
            .WithEnvironment("AUTH_SERVICE_KEY", serviceKey)
            .WithEnvironment("JWT_AUDIENCE", JwtAudience)
            .WithEnvironment("APPLE_BUNDLE_IDENTIFIER", appleBundleIdentifier)
            .WithEnvironment("GOOGLE_WEB_CLIENT_ID", googleWebClientId)
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
        auth.WithEnvironment("BETTER_AUTH_URL", auth.GetEndpoint("http"));

        if (builder.ExecutionContext.IsPublishMode)
        {
            // Publish only, in run mode the installer would remove biome and tsc from node_modules
            auth.WithEnvironment("PGUSER", identity.Resource.PrincipalName).WithNpm(installArgs: ["--omit=dev"]);
        }
        else
        {
            // Users in Azure are created by hand, only local runs and tests get a known user
            auth.WithEnvironment("SEED_USER_EMAIL", "test@murmli.de").WithEnvironment("SEED_USER_PASSWORD", "password");
        }

        return auth;
    }

    public static IResourceBuilder<ProjectResource> WithAuth(
        this IResourceBuilder<ProjectResource> api,
        IResourceBuilder<NodeAppResource> auth,
        IResourceBuilder<ParameterResource> serviceKey
    )
    {
        var endpoint = auth.GetEndpoint("http");
        api.WithEnvironment("Authentication__Schemes__Bearer__MetadataAddress", $"{endpoint}/.well-known/openid-configuration")
            .WithEnvironment("Authentication__Schemes__Bearer__ValidIssuer", endpoint)
            .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", JwtAudience)
            .WithEnvironment("AuthService__BaseAddress", endpoint)
            .WithEnvironment("AuthService__Key", serviceKey)
            .WaitFor(auth);

        // Locally the auth service serves plain http, which JwtBearer refuses for metadata by default
        if (api.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            api.WithEnvironment("Authentication__Schemes__Bearer__RequireHttpsMetadata", "false");
        }

        return api;
    }

    private static string RequiredConfigurationValue(IDistributedApplicationBuilder builder, string key)
    {
        var value = builder.Configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The configuration value {key} is not set.");
        }

        return value;
    }
}
