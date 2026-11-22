using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Murmli.Backend.Api.Database;
using Murmli.Backend.Api.Problems;
using Murmli.Backend.Api.Users;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.Processing;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Sync;

internal static class SyncEndpoints
{
    public const string DeviceIdHeader = "Murmli-Device-Id";

    // Unlike the web defaults, the contract matches frame names exactly and treats numbers written as strings as a wrong type
    private static readonly JsonSerializerOptions RequestSerializerOptions = new(JsonSerializerOptions.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
    };

    public static async Task<IResult> SyncAsync(
        HttpContext httpContext,
        [FromHeader(Name = DeviceIdHeader)] string? deviceId,
        [FromServices] SyncProcessor processor,
        [FromServices] IOptions<SyncOptions> syncOptions,
        [FromServices] IOptions<DatabaseOptions> databaseOptions
    )
    {
        if (httpContext.Features.Get<AccountFeature>() is not { } account)
        {
            return Problem(StatusCodes.Status410Gone, ProblemTypes.AccountGone);
        }

        // Routing already answered a present non-JSON Content-Type, only a missing one gets here
        if (!httpContext.Request.HasJsonContentType())
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, ProblemTypes.InvalidRequest);
        }

        if (!Guid.TryParseExact(deviceId, "D", out var device) || device.Version != 4)
        {
            return Problem(StatusCodes.Status400BadRequest, ProblemTypes.InvalidRequest);
        }

        var options = syncOptions.Value;
        SyncRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<SyncRequest>(
                httpContext.Request.Body,
                RequestSerializerOptions,
                httpContext.RequestAborted
            );
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return RequestTooLarge(options);
        }
        catch (Exception exception) when (exception is BadHttpRequestException or JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, ProblemTypes.InvalidRequest);
        }

        // The web defaults do not enforce nullable annotations
        if (request?.DataTypes is null || request.Commands is null || request.DataTypes.Any(dataType => dataType is null))
        {
            return Problem(StatusCodes.Status400BadRequest, ProblemTypes.InvalidRequest);
        }

        if (request.Commands.Count > options.MaxCommandsPerRequest)
        {
            return RequestTooLarge(options);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
        deadline.CancelAfter(options.RequestDeadline);
        SyncOutcome outcome;
        try
        {
            outcome = await processor.ProcessAsync(
                new SyncCaller(account.AccountId, device),
                request,
                GetAcceptedEncoding(httpContext.Request),
                deadline.Token
            );
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !httpContext.RequestAborted.IsCancellationRequested)
        {
            return new ServiceUnavailableProblem(databaseOptions.Value.UnavailableRetryAfter);
        }

        return outcome switch
        {
            SyncOutcome.Completed completed => new SyncResponseResult(completed.Body, completed.Encoding),
            SyncOutcome.AccountGone => Problem(StatusCodes.Status410Gone, ProblemTypes.AccountGone),
            SyncOutcome.DatabaseUnavailable => new ServiceUnavailableProblem(databaseOptions.Value.UnavailableRetryAfter),
            _ => throw new UnreachableException(),
        };
    }

    // "*" does not count as gzip, and an unreadable header means no compression (docs/sync.md "Kompression")
    private static SyncResponseEncoding GetAcceptedEncoding(HttpRequest request) =>
        StringWithQualityHeaderValue.TryParseStrictList(request.Headers.AcceptEncoding, out var codings)
        && codings.FirstOrDefault(coding => coding.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase)) is { } gzip
        && gzip.Quality.GetValueOrDefault(1) > 0
            ? SyncResponseEncoding.Gzip
            : SyncResponseEncoding.Identity;

    private static ProblemHttpResult Problem(int statusCode, string type) => TypedResults.Problem(statusCode: statusCode, type: type);

    private static ProblemHttpResult RequestTooLarge(SyncOptions options) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            type: ProblemTypes.RequestTooLarge,
            extensions: new Dictionary<string, object?>
            {
                ["maxRequestBytes"] = options.MaxRequestBytes,
                ["maxCommandsPerRequest"] = options.MaxCommandsPerRequest,
            }
        );
}
