using Aspire.Hosting;

namespace Murmli.Backend.Aspire.Tests.Infrastructure;

public sealed class AppHostFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));
        var cancellationToken = cancellationTokenSource.Token;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Murmli_Backend_Aspire>(
            ["--Parameters:better-auth-secret=murmli-test-better-auth-secret-0123456789", "--Parameters:auth-service-key=murmli-test-auth-service-key"],
            cancellationToken
        );
        App = await appHost.BuildAsync(cancellationToken);
        await App.StartAsync(cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("auth", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // xUnit also disposes a fixture whose InitializeAsync failed before the app was built
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}
