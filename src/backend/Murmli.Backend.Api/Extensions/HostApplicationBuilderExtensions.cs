using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Murmli.Backend.Api.Users;
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

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<UserRegistry>();
    }
}
