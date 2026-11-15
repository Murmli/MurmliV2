using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Murmli.Backend.Api.Users;

namespace Murmli.Backend.Api.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static void MapApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health").AllowAnonymous();

        var version1 = endpoints.MapGroup("/v1");
        version1.MapGet("/users/me", UserEndpoints.GetMeAsync);
        version1.MapDelete("/users/me", UserEndpoints.DeleteMeAsync);

        if (endpoints.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<bool>("OpenApi:Enabled"))
        {
            endpoints.MapOpenApi().AllowAnonymous();
        }
    }
}
