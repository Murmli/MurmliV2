using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests.Zitadel;

public class ExtensionsTests
{
    [Fact]
    public async Task WithZitadel_ConfiguredInstance_PassesAuthorityAndAudience()
    {
        var environment = await AppHostEnvironment.GetAsync(
            "api",
            ["--Zitadel:Authority=https://zitadel.test", "--Zitadel:ProjectId=123"]
        );

        Assert.Equal("https://zitadel.test", environment["Authentication__Schemes__Bearer__Authority"]);
        Assert.Equal("123", environment["Authentication__Schemes__Bearer__ValidAudiences__0"]);
    }
}
