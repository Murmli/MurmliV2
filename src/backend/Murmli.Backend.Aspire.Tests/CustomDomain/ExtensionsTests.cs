using Aspire.Hosting.ApplicationModel;
using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests.CustomDomain;

public class ExtensionsTests
{
    [Theory]
    [InlineData("api")]
    [InlineData("auth")]
    public async Task WithCustomDomain_RunMode_AddsNoParameters(string resourceName)
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain(
            appHost.Resources.OfType<ParameterResource>(),
            parameter => parameter.Name == $"{resourceName}-custom-domain" || parameter.Name == $"{resourceName}-certificate-name"
        );
    }

    [Theory]
    [InlineData("api", "")]
    [InlineData("api", "mc-api-certificate")]
    [InlineData("auth", "")]
    [InlineData("auth", "mc-auth-certificate")]
    public async Task WithCustomDomain_PublishMode_ResolvesConfiguredCertificateName(string resourceName, string certificateName)
    {
        await using var appHost = await AppHostEnvironment.CreatePublishModeAppHostAsync(
            [$"--Parameters:{resourceName}-custom-domain=custom.example.com", $"--Parameters:{resourceName}-certificate-name={certificateName}"]
        );

        var parameter = appHost.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == $"{resourceName}-certificate-name");
        Assert.Equal(certificateName, await parameter.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("auth")]
    public async Task WithCustomDomain_PublishModeWithoutCertificateName_ResolvesEmptyCertificateName(string resourceName)
    {
        await using var appHost = await AppHostEnvironment.CreatePublishModeAppHostAsync([$"--Parameters:{resourceName}-custom-domain=custom.example.com"]);

        var parameter = appHost.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == $"{resourceName}-certificate-name");
        Assert.Equal("", await parameter.GetValueAsync(TestContext.Current.CancellationToken));
    }
}
