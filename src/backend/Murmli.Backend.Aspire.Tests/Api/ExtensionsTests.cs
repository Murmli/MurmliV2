using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests.Api;

public class ExtensionsTests
{
    [Fact]
    public async Task AddApi_OpenApiEnabled_PassesSwitchToApi()
    {
        var environment = await AppHostEnvironment.GetAsync("api", ["--OpenApi:Enabled=true"]);

        Assert.Equal("true", environment["OpenApi__Enabled"]);
    }

    [Fact]
    public async Task AddApi_OpenApiDisabled_PassesNoSwitch()
    {
        var environment = await AppHostEnvironment.GetAsync("api", ["--OpenApi:Enabled=false"]);

        Assert.DoesNotContain("OpenApi__Enabled", environment.Keys);
    }
}
