using Aspire.Hosting.ApplicationModel;
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

    [Fact]
    public async Task AddApi_RunMode_AddsNoCustomDomainParameters()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain(
            appHost.Resources.OfType<ParameterResource>(),
            parameter => parameter.Name is "api-custom-domain" or "api-certificate-name"
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("mc-api-certificate")]
    public async Task AddApi_PublishMode_ResolvesConfiguredCertificateName(string certificateName)
    {
        await using var appHost = await AppHostEnvironment.CreatePublishModeAppHostAsync(
            ["--Parameters:api-custom-domain=api.example.com", $"--Parameters:api-certificate-name={certificateName}"]
        );

        var parameter = appHost.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == "api-certificate-name");
        Assert.Equal(certificateName, await parameter.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddApi_PublishModeWithoutCertificateName_ResolvesEmptyCertificateName()
    {
        await using var appHost = await AppHostEnvironment.CreatePublishModeAppHostAsync(["--Parameters:api-custom-domain=api.example.com"]);

        var parameter = appHost.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == "api-certificate-name");
        Assert.Equal("", await parameter.GetValueAsync(TestContext.Current.CancellationToken));
    }
}
