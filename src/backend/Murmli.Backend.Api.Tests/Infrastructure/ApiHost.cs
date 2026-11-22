using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Api.Users;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Sync.Extensions;
using Murmli.Backend.Telemetry.Extensions;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class ApiHost : IAsyncDisposable
{
    public const string AuthServiceKey = "test-service-key";

    public const string FailingEndpointPath = "/failure";

    public const string FailingEndpointMessage = "The endpoint failed on purpose.";

    public const string ExplicitlyAuthorizedEndpointPath = "/explicitly-authorized";

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

    public static async Task<ApiHost> StartAsync(
        string connectionString,
        IReadOnlyDictionary<string, string?>? additionalConfiguration = null,
        bool mapFailingEndpoint = false,
        ICollection<Activity>? activities = null,
        ICollection<LogRecord>? logRecords = null,
        bool mapExplicitlyAuthorizedEndpoint = false,
        bool testDataTypes = false
    )
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
        builder.AddTelemetry();
        builder.AddApi();
        if (testDataTypes)
        {
            builder.Services.ConfigureDbContext<MurmliDbContext>(options =>
                options.ReplaceService<IModelCustomizer, SyncTestModelCustomizer>()
            );
            builder.Services.AddSyncDataType<TestNoteType>().AddSyncDataType<TestBoardType>().AddSyncDataType<TestCardType>();
        }

        if (activities is not null)
        {
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(activities));
        }

        if (logRecords is not null)
        {
            builder.Services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(logRecords));
        }

        // Npgsql only writes UTC values to timestamp with time zone
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        builder.Services.AddSingleton<TimeProvider>(timeProvider);
        var authService = new AuthServiceHttpMessageHandler();
        builder.Services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = authService)
        );

        var application = builder.Build();
        await application.MigrateDatabaseAsync(cancellationToken);
        if (testDataTypes)
        {
            await using var scope = application.Services.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<MurmliDbContext>()
                .Database.ExecuteSqlRawAsync(SyncTestSchema.Ddl, cancellationToken);
        }

        application.UseApi();
        if (mapFailingEndpoint)
        {
            application.MapGet(FailingEndpointPath, void () => throw new InvalidOperationException(FailingEndpointMessage));
        }

        if (mapExplicitlyAuthorizedEndpoint)
        {
            application.MapGet(ExplicitlyAuthorizedEndpointPath, () => TypedResults.Ok()).RequireAuthorization();
        }

        await application.StartAsync(cancellationToken);

        return new ApiHost(
            application,
            new HttpClient { BaseAddress = new Uri(application.Urls.Single()) },
            timeProvider,
            authService
        );
    }

    public async Task<Guid> GetMeAsync(string authUserId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(authUserId));
        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(TestContext.Current.CancellationToken);
        return Assert.IsType<UserResponse>(user).Id;
    }

    public async Task<T> QueryDatabaseAsync<T>(Func<MurmliDbContext, Task<T>> query)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<MurmliDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();
        // Request spans end only after the response, a graceful stop lets them reach the exporters before disposal
        await application.StopAsync(TestContext.Current.CancellationToken);
        await application.DisposeAsync();
        AuthService.Dispose();
    }
}
