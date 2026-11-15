using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class ApiHost : IAsyncDisposable
{
    private readonly WebApplication application;

    private ApiHost(WebApplication application, HttpClient httpClient)
    {
        this.application = application;
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }

    public static async Task<ApiHost> StartAsync(string connectionString)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:murmli"] = connectionString,
                ["Authentication:Schemes:Bearer:ValidIssuer"] = TestTokens.Issuer,
                ["Authentication:Schemes:Bearer:ValidAudiences:0"] = TestTokens.Audience,
                ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = TestTokens.Issuer,
                ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = TestTokens.SigningKeyBase64,
            }
        );
        builder.AddApi();

        var application = builder.Build();
        await application.MigrateDatabaseAsync(cancellationToken);
        application.MapApi();
        await application.StartAsync(cancellationToken);

        return new ApiHost(application, new HttpClient { BaseAddress = new Uri(application.Urls.Single()) });
    }

    public async Task<T> QueryDatabaseAsync<T>(Func<MurmliDbContext, Task<T>> query)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<MurmliDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();
        await application.DisposeAsync();
    }
}
