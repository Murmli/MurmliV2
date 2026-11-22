using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Api.Tests.Extensions;

public class HostApplicationBuilderExtensionsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    [Fact]
    public async Task AddApi_ExplicitAuthorizationWithToken_ReturnsOk()
    {
        await using var api = await ApiHost.StartAsync(connectionString, mapExplicitlyAuthorizedEndpoint: true);

        using var response = await SendAsync(api, ApiHost.ExplicitlyAuthorizedEndpointPath, TestTokens.Create("100000000000000001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(ApiHost.ExplicitlyAuthorizedEndpointPath)]
    [InlineData("/v1/users/me")]
    public async Task AddApi_TokenWithoutSubject_ReturnsForbidden(string path)
    {
        await using var api = await ApiHost.StartAsync(connectionString, mapExplicitlyAuthorizedEndpoint: true);

        using var response = await SendAsync(api, path, TestTokens.CreateWithoutSubject());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Database:MaxRetryCount", "11", "MaxRetryCount")]
    [InlineData("Database:MaxRetryDelay", "00:00:31", "MaxRetryDelay")]
    public async Task AddApi_DatabaseRetryOptionInvalid_FailsOptionsValidationWhenResolvingContext(string key, string value, string expectedError)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:murmli"] = "Host=localhost;Database=murmli;Username=postgres;Password=postgres",
                [key] = value,
            }
        );
        builder.AddApi();
        await using var application = builder.Build();
        await using var scope = application.Services.CreateAsyncScope();

        var exception = Assert.Throws<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<MurmliDbContext>());

        Assert.Contains(expectedError, exception.Message);
    }

    [Fact]
    public async Task AddApi_SyncFieldNullabilityDiffersFromModel_FailsOptionsValidation()
    {
        var builder = CreateApiBuilder();
        builder.Services.ConfigureDbContext<MurmliDbContext>(options =>
            options.ReplaceService<IModelCustomizer, SyncTestModelCustomizer>()
        );
        builder.Services.AddSyncDataType<NullableTitleNoteType>();
        await using var application = builder.Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            application.Services.GetRequiredService<IOptions<SyncOptions>>().Value
        );

        Assert.Contains("brokenNote", exception.Message);
        Assert.Contains("title", exception.Message);
    }

    [Fact]
    public async Task AddApi_FullStateCompressionLevelUndefined_FailsOptionsValidation()
    {
        await using var application = CreateApiBuilder(new Dictionary<string, string?> { ["Sync:FullStateCompressionLevel"] = "5" }).Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            application.Services.GetRequiredService<IOptions<SyncOptions>>().Value
        );

        Assert.Contains(nameof(SyncOptions.FullStateCompressionLevel), exception.Message);
    }

    private static WebApplicationBuilder CreateApiBuilder(IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:murmli"] = "Host=localhost;Database=murmli;Username=postgres;Password=postgres",
            }
        );
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddApi();
        return builder;
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
