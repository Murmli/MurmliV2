using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning.OperationalInsights;
using Microsoft.Extensions.Configuration;

namespace Murmli.Backend.Aspire.ApplicationInsights;

public static class Extensions
{
    public static IResourceBuilder<AzureApplicationInsightsResource>? AddApplicationInsights(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzureContainerAppEnvironmentResource> environment
    )
    {
        if (!builder.ExecutionContext.IsPublishMode)
        {
            return null;
        }

        if (builder.Configuration.GetValue<double?>("LogAnalytics:DailyQuotaGb") is { } dailyQuotaGb)
        {
            environment.ConfigureInfrastructure(infrastructure =>
                infrastructure.GetProvisionableResources().OfType<OperationalInsightsWorkspace>().Single().WorkspaceCapping = new()
                {
                    DailyQuotaInGB = dailyQuotaGb,
                }
            );
        }

        // The environment creates its workspace implicitly, Aspire has no public accessor for this output name
        return builder
            .AddAzureApplicationInsights("app-insights")
            .WithLogAnalyticsWorkspace(environment.GetOutput("AZURE_LOG_ANALYTICS_WORKSPACE_ID"));
    }
}
