using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Murmli.Backend.Api.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static void MapApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health");
    }
}
