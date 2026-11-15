namespace Murmli.Backend.Aspire.Zitadel;

public static class Extensions
{
    public static IResourceBuilder<ProjectResource> WithZitadel(this IResourceBuilder<ProjectResource> resource) =>
        resource
            .WithEnvironment("Authentication__Schemes__Bearer__Authority", GetRequiredValue(resource.ApplicationBuilder, "Zitadel:Authority"))
            .WithEnvironment("Authentication__Schemes__Bearer__ValidAudiences__0", GetRequiredValue(resource.ApplicationBuilder, "Zitadel:ProjectId"));

    private static string GetRequiredValue(IDistributedApplicationBuilder builder, string key)
    {
        var value = builder.Configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The configuration value '{key}' is missing for the AppHost environment '{builder.Environment.EnvironmentName}'. Set it in appsettings.{builder.Environment.EnvironmentName}.json of the AppHost or pass --environment azure-<name>."
            );
        }

        return value;
    }
}
