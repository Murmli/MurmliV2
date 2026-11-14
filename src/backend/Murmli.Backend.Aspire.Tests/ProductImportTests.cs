using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Murmli.Backend.Aspire.Tests;

public class ProductImportTests
{
    [Fact]
    public async Task AddProductImport_ProductLimitConfigured_PassesLimitToWorker()
    {
        var environment = await GetProductImportEnvironmentAsync(["--ProductImport:ProductLimit=100"]);

        Assert.Equal("100", environment["ProductImport__ProductLimit"]);
    }

    [Fact]
    public async Task AddProductImport_NoProductLimit_PassesNoLimitToWorker()
    {
        var environment = await GetProductImportEnvironmentAsync([]);

        Assert.DoesNotContain("ProductImport__ProductLimit", environment.Keys);
    }

    private static async Task<Dictionary<string, string>> GetProductImportEnvironmentAsync(string[] args)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(args, cancellationToken);
        await using var app = await appHost.BuildAsync(cancellationToken);
        var productImport = appHost.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "product-import");

        // Run mode would resolve endpoints that only exist in a started app, publish mode yields expressions instead
        var configuration = await ExecutionConfigurationBuilder
            .Create(productImport)
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
