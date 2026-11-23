using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Api.Tests.Infrastructure;

[assembly: AssemblyFixture(typeof(OptionsValidationFixture))]

namespace Murmli.Backend.Api.Tests.Infrastructure;

// RangeAttribute converts its string bounds on first use without a lock, hosts starting in parallel race on the shared attribute instances
public sealed class OptionsValidationFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:murmli"] = "Host=localhost;Database=murmli;Username=postgres;Password=postgres",
                ["AuthService:BaseAddress"] = "http://auth.test",
                ["AuthService:Key"] = ApiHost.AuthServiceKey,
            }
        );
        builder.AddApi();
        await using var application = builder.Build();
        foreach (var startupValidator in application.Services.GetServices<IAsyncStartupValidator>())
        {
            await startupValidator.ValidateAsync(TestContext.Current.CancellationToken);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
