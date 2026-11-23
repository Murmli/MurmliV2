using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Murmli.Backend.Api.Problems;

internal sealed class RateLimitedProblem(TimeSpan retryAfter) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return TypedResults
            .Problem(statusCode: StatusCodes.Status429TooManyRequests, type: ProblemTypes.RateLimited)
            .ExecuteAsync(httpContext);
    }
}
