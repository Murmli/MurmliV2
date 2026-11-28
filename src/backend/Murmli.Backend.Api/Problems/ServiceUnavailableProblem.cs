using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Murmli.Backend.Api.Problems;

internal sealed class ServiceUnavailableProblem(TimeSpan minimumRetryAfter) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        var minimumSeconds = (int)Math.Ceiling(minimumRetryAfter.TotalSeconds);
        // Spreads waiting devices so they do not all return at the same moment
        var seconds = Random.Shared.Next(minimumSeconds, 2 * minimumSeconds + 1);
        httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return TypedResults
            .Problem(statusCode: StatusCodes.Status503ServiceUnavailable, type: ProblemTypes.ServiceUnavailable)
            .ExecuteAsync(httpContext);
    }
}
