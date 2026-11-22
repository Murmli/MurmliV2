using Microsoft.AspNetCore.Http;

namespace Murmli.Backend.Api.Sync;

internal sealed class SyncResponseResult(ReadOnlyMemory<byte> body) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        httpContext.Response.Headers.Vary = "Accept-Encoding";
        httpContext.Response.ContentLength = body.Length;
        await httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted);
    }
}
