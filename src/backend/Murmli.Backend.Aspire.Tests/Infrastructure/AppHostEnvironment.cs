using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Murmli.Backend.Aspire.Tests.Infrastructure;

internal static class AppHostEnvironment
{
    public static async Task<Dictionary<string, string>> GetAsync(string resourceName, string[] args)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(args, cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken);
        var project = appHost.Resources.OfType<ProjectResource>().Single(resource => resource.Name == resourceName);

        // Run mode would resolve endpoints that only exist in a started app, publish mode yields expressions instead
        var configuration = await ExecutionConfigurationBuilder
            .Create(project)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new DistributedApplicationExecutionContext(
                    new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish) { Services = app.Services }
                ),
                cancellationToken: cancellationToken
            );

        Assert.Null(configuration.Exception);
        return configuration.EnvironmentVariables.ToDictionary();
    }
}
