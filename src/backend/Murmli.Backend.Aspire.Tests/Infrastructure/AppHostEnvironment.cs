using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Murmli.Backend.Aspire.Tests.Infrastructure;

internal static class AppHostEnvironment
{
    // --clear-cache keeps the model from reading local deployment state
    public static readonly string[] PublishModeArguments = ["--operation", "publish", "--clear-cache", "true"];

    public static Task<IDistributedApplicationTestingBuilder> CreatePublishModeAppHostAsync(string[] args) =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(
            [.. PublishModeArguments, .. args],
            TestContext.Current.CancellationToken
        );

    public static async Task<Dictionary<string, string>> GetAsync(string resourceName, string[] args)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(args, cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken);
        var resource = appHost.Resources.Single(resource => resource.Name == resourceName);

        // Run mode would resolve endpoints that only exist in a started app, publish mode yields expressions instead
        var configuration = await ExecutionConfigurationBuilder
            .Create(resource)
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
