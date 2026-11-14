using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests.ApplicationInsights;

public class ExtensionsTests
{
    [Fact]
    public async Task AddApplicationInsights_RunMode_AddsNoApplicationInsights()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(
            TestContext.Current.CancellationToken
        );

        Assert.Empty(appHost.Resources.OfType<AzureApplicationInsightsResource>());
    }

    [Fact]
    public async Task AddApplicationInsights_DailyQuotaConfigured_CapsWorkspace()
    {
        await using var appHost = await CreatePublishModeAppHostAsync(["--LogAnalytics:DailyQuotaGb=0.2"]);

        var environment = appHost.Resources.OfType<AzureContainerAppEnvironmentResource>().Single();
        Assert.Contains("dailyQuotaGb: json('0.2')", environment.GetBicepTemplateString());
    }

    [Fact]
    public async Task AddApplicationInsights_NoDailyQuota_LeavesWorkspaceUncapped()
    {
        await using var appHost = await CreatePublishModeAppHostAsync([]);

        var environment = appHost.Resources.OfType<AzureContainerAppEnvironmentResource>().Single();
        Assert.DoesNotContain("dailyQuotaGb", environment.GetBicepTemplateString());
    }

    [Fact]
    public async Task AddApplicationInsights_PublishMode_UsesWorkspaceOfEnvironment()
    {
        await using var appHost = await CreatePublishModeAppHostAsync([]);

        var environment = appHost.Resources.OfType<AzureContainerAppEnvironmentResource>().Single();
        var applicationInsights = appHost.Resources.OfType<AzureApplicationInsightsResource>().Single();
        var template = applicationInsights.GetBicepTemplateString();
        Assert.DoesNotContain("Microsoft.OperationalInsights/workspaces", template);
        Assert.Contains("WorkspaceResourceId: env_outputs_azure_log_analytics_workspace_id", template);
        var workspaceId = Assert.IsType<BicepOutputReference>(applicationInsights.Parameters["env_outputs_azure_log_analytics_workspace_id"]);
        Assert.Equal("AZURE_LOG_ANALYTICS_WORKSPACE_ID", workspaceId.Name);
        Assert.Same(environment, workspaceId.Resource);
    }

    private static Task<IDistributedApplicationTestingBuilder> CreatePublishModeAppHostAsync(string[] args) =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(
            [.. AppHostEnvironment.PublishModeArguments, .. args],
            TestContext.Current.CancellationToken
        );
}
