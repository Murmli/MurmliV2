using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests.Telemetry;

public class ExtensionsTests
{
    [Theory]
    [InlineData("api")]
    [InlineData("product-import")]
    public async Task WithTelemetry_SensitiveDataEnabled_PassesSwitchToApps(string resourceName)
    {
        var environment = await AppHostEnvironment.GetAsync(resourceName, ["--Telemetry:RecordSensitiveData=true"]);

        Assert.Equal("true", environment["Telemetry__RecordSensitiveData"]);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("product-import")]
    public async Task WithTelemetry_SensitiveDataDisabled_PassesNoSwitch(string resourceName)
    {
        var environment = await AppHostEnvironment.GetAsync(resourceName, ["--Telemetry:RecordSensitiveData=false"]);

        Assert.DoesNotContain("Telemetry__RecordSensitiveData", environment.Keys);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("product-import")]
    [InlineData("auth")]
    public async Task WithTelemetry_PublishMode_ConnectsAppsToApplicationInsights(string resourceName)
    {
        var environment = await AppHostEnvironment.GetAsync(resourceName, AppHostEnvironment.PublishModeArguments);

        Assert.Contains("APPLICATIONINSIGHTS_CONNECTION_STRING", environment.Keys);
        Assert.Equal(resourceName, environment["OTEL_SERVICE_NAME"]);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("product-import")]
    public async Task WithTelemetry_PublishMode_LimitsConsoleToWarnings(string resourceName)
    {
        var environment = await AppHostEnvironment.GetAsync(resourceName, AppHostEnvironment.PublishModeArguments);

        Assert.Equal("Warning", environment["Logging__Console__LogLevel__Default"]);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("product-import")]
    [InlineData("auth")]
    public async Task WithTelemetry_RunMode_PassesNoApplicationInsightsSettings(string resourceName)
    {
        var environment = await AppHostEnvironment.GetAsync(resourceName, []);

        Assert.DoesNotContain("APPLICATIONINSIGHTS_CONNECTION_STRING", environment.Keys);
        Assert.DoesNotContain("OTEL_SERVICE_NAME", environment.Keys);
        Assert.DoesNotContain("Logging__Console__LogLevel__Default", environment.Keys);
    }
}
