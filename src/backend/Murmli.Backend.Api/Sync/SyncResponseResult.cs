using Microsoft.AspNetCore.Http;
using Murmli.Backend.Sync.Processing;

namespace Murmli.Backend.Api.Sync;

internal sealed class SyncResponseResult(ReadOnlyMemory<byte> body, SyncResponseEncoding encoding) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        httpContext.Response.Headers.Vary = "Accept-Encoding";
        if (encoding == SyncResponseEncoding.Gzip)
        {
            httpContext.Response.Headers.ContentEncoding = "gzip";
        }

        httpContext.Response.ContentLength = body.Length;
        await httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted);
    }
}
