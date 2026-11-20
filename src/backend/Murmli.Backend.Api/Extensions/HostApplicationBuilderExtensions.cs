using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Murmli.Backend.Api.AuthService;
using Murmli.Backend.Api.Products;
using Murmli.Backend.Api.Users;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Api.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddApi(this IHostApplicationBuilder builder)
    {
        builder.AddMurmliDbContext();

        builder.Services.AddAuthentication().AddJwtBearer(options => options.MapInboundClaims = false);
        var authenticatedUserWithSubjectPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(JwtRegisteredClaimNames.Sub)
            .Build();
        builder
            .Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(authenticatedUserWithSubjectPolicy)
            .SetFallbackPolicy(authenticatedUserWithSubjectPolicy);

        builder
            .Services.AddOptions<AuthServiceOptions>()
            .BindConfiguration(AuthServiceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddHttpClient<AuthServiceClient>(
            (services, client) =>
            {
                var options = services.GetRequiredService<IOptions<AuthServiceOptions>>().Value;
                client.BaseAddress = options.BaseAddress;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Key);
                client.Timeout = TimeSpan.FromSeconds(10);
            }
        );

        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<UserRegistry>();
        builder.Services.AddScoped<ProductSearch>();
    }
}
