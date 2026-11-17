using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class ApiHost : IAsyncDisposable
{
    public const string AuthServiceKey = "test-service-key";

    private readonly WebApplication application;

    private ApiHost(WebApplication application, HttpClient httpClient, FakeTimeProvider timeProvider, AuthServiceHttpMessageHandler authService)
    {
        this.application = application;
        HttpClient = httpClient;
        TimeProvider = timeProvider;
        AuthService = authService;
    }

    public HttpClient HttpClient { get; }

    public FakeTimeProvider TimeProvider { get; }

    public AuthServiceHttpMessageHandler AuthService { get; }

    public static async Task<ApiHost> StartAsync(string connectionString, IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
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
                ["AuthService:BaseAddress"] = "http://auth.test",
                ["AuthService:Key"] = AuthServiceKey,
            }
        );
        builder.Configuration.AddInMemoryCollection(additionalConfiguration);
        builder.AddApi();

        // Npgsql only writes UTC values to timestamp with time zone
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        builder.Services.AddSingleton<TimeProvider>(timeProvider);
        var authService = new AuthServiceHttpMessageHandler();
        builder.Services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = authService)
        );

        var application = builder.Build();
        await application.MigrateDatabaseAsync(cancellationToken);
        application.MapApi();
        await application.StartAsync(cancellationToken);

        return new ApiHost(
            application,
            new HttpClient { BaseAddress = new Uri(application.Urls.Single()) },
            timeProvider,
            authService
        );
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
        AuthService.Dispose();
    }
}
