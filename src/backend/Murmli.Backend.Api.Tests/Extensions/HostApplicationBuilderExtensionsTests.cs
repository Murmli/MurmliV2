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
        await using var application = CreateApiBuilder(new Dictionary<string, string?> { [key] = value }).Build();
        await using var scope = application.Services.CreateAsyncScope();

        var exception = Assert.Throws<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<MurmliDbContext>());

        Assert.Contains(expectedError, exception.Message);
    }

    [Fact]
    public async Task AddApi_SyncFieldNullabilityDiffersFromModel_FailsOptionsValidation()
    {
        var builder = CreateSyncTestApiBuilder();
        builder.Services.AddSyncDataType<NullableTitleNoteType>();
        await using var application = builder.Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            application.Services.GetRequiredService<IOptions<SyncOptions>>().Value
        );

        Assert.Contains("brokenNote", exception.Message);
        Assert.Contains("title", exception.Message);
    }

    [Theory]
    [InlineData("rootInPersonalScope", "'personalRootNote' is a scope root of the scope kind 'personal'")]
    [InlineData("sharedKindWithoutRoot", "The scope kind 'testBoard' has no scope root type.")]
    [InlineData("sharedKindWithTwoRoots", "The scope kind 'testBoard' has more than one scope root type: 'testBoard', 'secondBoard'.")]
    [InlineData("fillTypeMismatch", "uses the fill ActingMemberOnApply, which needs a property of type Guid?")]
    [InlineData(
        "commandTimeFillTypeMismatch",
        "The server field 'createdAt' of the data type 'misfilledTimeCard' (MisfilledTimeCardType) uses the fill CommandTimeOnCreate, which needs a property of type DateTimeOffset"
    )]
    [InlineData(
        "commandTimeOnApplyFillTypeMismatch",
        "The server field 'changedAt' of the data type 'misfilledChangeTimeCard' (MisfilledChangeTimeCardType) uses the fill CommandTimeOnApply, which needs a property of type DateTimeOffset"
    )]
    [InlineData(
        "memberReferenceOnDeviceField",
        "The field 'changedBy' of the data type 'deviceReferenceCard' (DeviceReferenceCardType) is a member reference"
    )]
    [InlineData(
        "createOnlyKeyWithoutRequiredOnCreate",
        "The field 'category' of the data type 'optionalCreateOnlyKeyCard' (OptionalCreateOnlyKeyCardType) has no default on create, so it must be RequiredOnCreate."
    )]
    [InlineData(
        "emptyRejectingPatternWithoutRequiredOnCreate",
        "The field 'text' of the data type 'optionalPatternTextCard' (OptionalPatternTextCardType) has no default on create, so it must be RequiredOnCreate."
    )]
    [InlineData(
        "timestampServerFieldWithoutFill",
        "The server field 'createdAt' of the data type 'unfilledCard' (UnfilledCreatedAtCardType) has no default on create"
    )]
    [InlineData("ownedScopeLimitOnNonRoot", "Sync:DataTypes:testNote:MaxOwnedScopesPerAccount applies only to scope root types.")]
    [InlineData("ownedScopeLimitBelowOne", "Sync:DataTypes:testBoard:MaxOwnedScopesPerAccount must be at least 1.")]
    [InlineData("scopeRootBelowOne", "The data type 'zeroRootBoard' (ZeroRootBoardType) must call ScopeRoot with a value of at least 1.")]
    [InlineData(
        "createRoleBelowSetFieldsRole",
        "The data type 'lowerCreateRoleCard' (LowerCreateRoleCardType) requires the role 'member' for create, which does not satisfy the role 'owner' for setFields"
    )]
    public async Task AddApi_SyncDataTypeRegistrationBroken_FailsOptionsValidation(string scenario, string expectedError)
    {
        var builder = CreateSyncTestApiBuilder(BrokenConfigurationOf(scenario));
        AddBrokenRegistration(builder, scenario);
        await using var application = builder.Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            application.Services.GetRequiredService<IOptions<SyncOptions>>().Value
        );

        Assert.Contains(expectedError, exception.Message);
    }

    [Fact]
    public async Task AddApi_CreateRoleUnknownToCore_PassesOptionsValidation()
    {
        var builder = CreateSyncTestApiBuilder();
        builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<ClosedCreateCardType>();
        await using var application = builder.Build();

        var exception = Record.Exception(() => application.Services.GetRequiredService<IOptions<SyncOptions>>().Value);

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Sync:FullStateCompressionLevel", "5", "FullStateCompressionLevel")]
    [InlineData("Sync:Cleanup:SafetyMargin", "00:04:59", "Sync:Cleanup:SafetyMargin must be at least Sync:ClockAheadTolerance plus Sync:RequestDeadline.")]
    [InlineData("Sync:Cleanup:SafetyMargin", "00:05:44", "Sync:Cleanup:SafetyMargin must be at least Sync:ClockAheadTolerance plus Sync:RequestDeadline.")]
    [InlineData("Sync:Cleanup:LeaseDuration", "00:01:59", "LeaseDuration")]
    [InlineData("Sync:RateLimits:AccountCommandsPerHour", "499", "Sync:RateLimits:AccountCommandsPerHour must be at least Sync:MaxCommandsPerRequest.")]
    [InlineData("Sync:RateLimits:DeviceRequestsPerMinute", "0", "DeviceRequestsPerMinute")]
    [InlineData("Sync:RateLimits:AccountRequestsPerMinute", "0", "AccountRequestsPerMinute")]
    public async Task AddApi_SyncOptionInvalid_FailsOptionsValidation(string key, string value, string expectedError)
    {
        await using var application = CreateApiBuilder(new Dictionary<string, string?> { [key] = value }).Build();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            application.Services.GetRequiredService<IOptions<SyncOptions>>().Value
        );

        Assert.Contains(expectedError, exception.Message);
    }

    [Fact]
    public async Task AddApi_ShoppingListOptionInvalid_FailsStartupValidation()
    {
        await using var application = CreateApiBuilder(
                new Dictionary<string, string?>
                {
                    ["ShoppingLists:CheckedItemRetention"] = "00:00:59",
                    ["AuthService:BaseAddress"] = "http://auth.test",
                    ["AuthService:Key"] = ApiHost.AuthServiceKey,
                }
            )
            .Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            foreach (var startupValidator in application.Services.GetServices<IAsyncStartupValidator>())
            {
                await startupValidator.ValidateAsync(TestContext.Current.CancellationToken);
            }
        });

        Assert.Contains("CheckedItemRetention", exception.Message);
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

    private static WebApplicationBuilder CreateSyncTestApiBuilder(IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var builder = CreateApiBuilder(configuration);
        builder.Services.ConfigureDbContext<MurmliDbContext>(options =>
            options.ReplaceService<IModelCustomizer, SyncTestModelCustomizer>()
        );
        return builder;
    }

    private static void AddBrokenRegistration(WebApplicationBuilder builder, string scenario)
    {
        switch (scenario)
        {
            case "rootInPersonalScope":
                builder.Services.AddSyncDataType<PersonalRootNoteType>();
                break;
            case "sharedKindWithoutRoot":
                builder.Services.AddSyncDataType<TestCardType>();
                break;
            case "sharedKindWithTwoRoots":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<SecondRootBoardType>().AddSyncDataType<TestCardType>();
                break;
            case "fillTypeMismatch":
                builder.Services.AddSyncDataType<MisfilledNoteType>();
                break;
            case "commandTimeFillTypeMismatch":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<MisfilledTimeCardType>();
                break;
            case "commandTimeOnApplyFillTypeMismatch":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<MisfilledChangeTimeCardType>();
                break;
            case "memberReferenceOnDeviceField":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<DeviceReferenceCardType>();
                break;
            case "createOnlyKeyWithoutRequiredOnCreate":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<OptionalCreateOnlyKeyCardType>();
                break;
            case "emptyRejectingPatternWithoutRequiredOnCreate":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<OptionalPatternTextCardType>();
                break;
            case "timestampServerFieldWithoutFill":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<UnfilledCreatedAtCardType>();
                break;
            case "ownedScopeLimitOnNonRoot":
            case "ownedScopeLimitBelowOne":
                builder.Services.AddSyncDataType<TestNoteType>().AddSyncDataType<TestBoardType>().AddSyncDataType<TestCardType>();
                break;
            case "scopeRootBelowOne":
                builder.Services.AddSyncDataType<ZeroRootBoardType>();
                break;
            case "createRoleBelowSetFieldsRole":
                builder.Services.AddSyncDataType<TestBoardType>().AddSyncDataType<LowerCreateRoleCardType>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "The scenario names no broken registration.");
        }
    }

    private static Dictionary<string, string?>? BrokenConfigurationOf(string scenario) =>
        scenario switch
        {
            "ownedScopeLimitOnNonRoot" => new() { ["Sync:DataTypes:testNote:MaxOwnedScopesPerAccount"] = "1" },
            "ownedScopeLimitBelowOne" => new() { ["Sync:DataTypes:testBoard:MaxOwnedScopesPerAccount"] = "0" },
            _ => null,
        };

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
