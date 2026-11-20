using Aspire.Hosting.JavaScript;
using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests.Auth;

public class ExtensionsTests
{
    [Fact]
    public async Task AddAuth_PublishMode_PassesDatabaseUserAndNoSeedUser()
    {
        var environment = await AppHostEnvironment.GetAsync("auth", AppHostEnvironment.PublishModeArguments);

        Assert.Equal("{backend-identity.outputs.principalName}", environment["PGUSER"]);
        Assert.DoesNotContain("SEED_USER_EMAIL", environment.Keys);
        Assert.DoesNotContain("SEED_USER_PASSWORD", environment.Keys);
    }

    [Fact]
    public async Task AddAuth_RunMode_PassesSeedUserAndNoDatabaseUser()
    {
        var environment = await AppHostEnvironment.GetAsync("auth", []);

        Assert.Equal("test@murmli.de", environment["SEED_USER_EMAIL"]);
        Assert.Equal("password", environment["SEED_USER_PASSWORD"]);
        Assert.DoesNotContain("PGUSER", environment.Keys);
    }

    [Fact]
    public async Task AddAuth_PublishMode_InstallsWithoutDevDependencies()
    {
        var arguments = await GetInstallArgumentsAsync(AppHostEnvironment.PublishModeArguments);

        Assert.Contains("--omit=dev", arguments);
    }

    [Fact]
    public async Task AddAuth_RunMode_InstallsDevDependencies()
    {
        var arguments = await GetInstallArgumentsAsync([]);

        Assert.DoesNotContain("--omit=dev", arguments);
    }

    [Fact]
    public async Task AddAuth_SecretsSet_PassesParameterReferencesInsteadOfValues()
    {
        string[] arguments = ["--Parameters:better-auth-secret=plain-text-secret", "--Parameters:auth-service-key=plain-text-key"];

        var authEnvironment = await AppHostEnvironment.GetAsync("auth", arguments);
        var apiEnvironment = await AppHostEnvironment.GetAsync("api", arguments);

        Assert.Equal("{better-auth-secret.value}", authEnvironment["BETTER_AUTH_SECRET"]);
        Assert.Equal("{auth-service-key.value}", authEnvironment["AUTH_SERVICE_KEY"]);
        Assert.Equal("{auth-service-key.value}", apiEnvironment["AuthService__Key"]);
    }

    [Fact]
    public async Task AddAuth_AppleBundleIdentifierSet_PassesItToAuth()
    {
        var environment = await AppHostEnvironment.GetAsync("auth", ["--Apple:BundleIdentifier=de.example.app"]);

        Assert.Equal("de.example.app", environment["APPLE_BUNDLE_IDENTIFIER"]);
    }

    [Fact]
    public async Task AddAuth_AppleBundleIdentifierEmpty_Throws()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => AppHostEnvironment.GetAsync("auth", ["--Apple:BundleIdentifier="]));

        Assert.Contains("Apple:BundleIdentifier", exception.Message);
    }

    [Fact]
    public async Task WithAuth_RunMode_PointsApiAtAuthService()
    {
        var authEnvironment = await AppHostEnvironment.GetAsync("auth", []);
        var apiEnvironment = await AppHostEnvironment.GetAsync("api", []);

        Assert.Equal(
            "{auth.bindings.http.url}/.well-known/openid-configuration",
            apiEnvironment["Authentication__Schemes__Bearer__MetadataAddress"]
        );
        Assert.Equal("{auth.bindings.http.url}", apiEnvironment["Authentication__Schemes__Bearer__ValidIssuer"]);
        Assert.Equal("{auth.bindings.http.url}", apiEnvironment["AuthService__BaseAddress"]);
        Assert.Equal("false", apiEnvironment["Authentication__Schemes__Bearer__RequireHttpsMetadata"]);
        Assert.Equal(authEnvironment["BETTER_AUTH_URL"], apiEnvironment["Authentication__Schemes__Bearer__ValidIssuer"]);
        Assert.Equal(authEnvironment["JWT_AUDIENCE"], apiEnvironment["Authentication__Schemes__Bearer__ValidAudiences__0"]);
    }

    [Fact]
    public async Task WithAuth_PublishMode_RequiresHttpsMetadata()
    {
        var environment = await AppHostEnvironment.GetAsync("api", AppHostEnvironment.PublishModeArguments);

        Assert.DoesNotContain("Authentication__Schemes__Bearer__RequireHttpsMetadata", environment.Keys);
    }

    private static async Task<string[]> GetInstallArgumentsAsync(string[] arguments)
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(
            arguments,
            TestContext.Current.CancellationToken
        );

        var auth = appHost.Resources.Single(resource => resource.Name == "auth");
        return auth.Annotations.OfType<JavaScriptInstallCommandAnnotation>().Last().Args;
    }
}
