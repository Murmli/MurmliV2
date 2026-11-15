using System.Collections.Concurrent;
using System.Net;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class ZitadelHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<ZitadelRequest> requests = new();

    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

    public IReadOnlyCollection<ZitadelRequest> Requests => requests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requests.Enqueue(new ZitadelRequest(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
        return Task.FromResult(new HttpResponseMessage(ResponseStatusCode));
    }
}
