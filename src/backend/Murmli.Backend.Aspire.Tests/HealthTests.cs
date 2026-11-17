using System.Net;
using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests;

[Collection<AppHostCollection>]
public class HealthTests(AppHostFixture appHost)
{
    [Fact(Timeout = 60_000)]
    public async Task GetHealth_DatabaseRunning_ReturnsHealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var httpClient = appHost.App.CreateHttpClient("api");
        using var response = await httpClient.GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
