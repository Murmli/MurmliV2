using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Murmli.Backend.Api.Products;
using Murmli.Backend.Api.Users;
using Murmli.Backend.Api.Zitadel;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Api.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddApi(this IHostApplicationBuilder builder)
    {
        builder.AddMurmliDbContext();

        builder.Services.AddAuthentication().AddJwtBearer(options => options.MapInboundClaims = false);
        builder
            .Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(JwtRegisteredClaimNames.Sub).Build());

        builder
            .Services.AddOptions<ZitadelOptions>()
            .BindConfiguration(ZitadelOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddHttpClient<ZitadelUserClient>(
            (services, client) =>
            {
                var options = services.GetRequiredService<IOptions<ZitadelOptions>>().Value;
                client.BaseAddress = options.Authority;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ServiceUserToken);
                client.Timeout = TimeSpan.FromSeconds(10);
            }
        );

        builder.Services.AddOpenApi();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<UserRegistry>();
        builder.Services.AddScoped<ProductSearch>();
    }
}
