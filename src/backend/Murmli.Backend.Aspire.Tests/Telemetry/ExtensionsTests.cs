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
}
