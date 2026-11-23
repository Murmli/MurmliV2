using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Murmli.Backend.Api.Products;
using Murmli.Backend.Api.Sync;
using Murmli.Backend.Api.Users;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.Protocol;

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
        var syncOptions = application.Services.GetRequiredService<IOptions<SyncOptions>>().Value;
        version1
            .MapPost("/sync", SyncEndpoints.SyncAsync)
            .Accepts<SyncRequest>("application/json")
            .Produces<SyncResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithDescription(
                "Processes the device's waiting commands and returns the changes since its token. The contract, the fields per data type and the problem types are described in docs/sync.md."
            )
            .WithMetadata(new RequestSizeLimitAttribute(syncOptions.MaxRequestBytes));

        if (application.Configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            application.MapOpenApi().AllowAnonymous();
        }
    }
}
