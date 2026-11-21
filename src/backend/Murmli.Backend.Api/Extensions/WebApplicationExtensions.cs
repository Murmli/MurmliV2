using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Murmli.Backend.Api.Products;
using Murmli.Backend.Api.Shopping;
using Murmli.Backend.Api.Users;

namespace Murmli.Backend.Api.Extensions;

public static class WebApplicationExtensions
{
    public static void UseApi(this WebApplication application)
    {
        // WebApplication inserts authentication and authorization before the middleware registered here
        application.UseMiddleware<UserTelemetryMiddleware>();
        // Inside the UserId scope, otherwise unhandled exceptions are logged without it
        application.UseExceptionHandler();

        application.MapHealthChecks("/health").AllowAnonymous();

        var version1 = application.MapGroup("/v1");
        version1.MapGet("/users/me", UserEndpoints.GetMeAsync);
        version1.MapDelete("/users/me", UserEndpoints.DeleteMeAsync);
        version1.MapGet("/products", ProductEndpoints.SearchAsync);
        version1.MapPost("/sync", ShoppingEndpoints.SyncAsync);
        version1.MapGet("/sync/events", ShoppingEndpoints.StreamEventsAsync);
        version1.MapPost("/lists/{listId:guid}/members", ShoppingEndpoints.AddMemberAsync);
        version1.MapDelete("/lists/{listId:guid}/members/{userId:guid}", ShoppingEndpoints.RemoveMemberAsync);

        if (application.Configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            application.MapOpenApi().AllowAnonymous();
        }
    }
}
