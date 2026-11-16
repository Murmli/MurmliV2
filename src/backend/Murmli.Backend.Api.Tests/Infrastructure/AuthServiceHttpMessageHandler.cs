using System.Collections.Concurrent;
using System.Net;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class AuthServiceHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<AuthServiceRequest> requests = new();

    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.NoContent;

    public IReadOnlyCollection<AuthServiceRequest> Requests => requests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        requests.Enqueue(
            new AuthServiceRequest(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body)
        );
        return new HttpResponseMessage(ResponseStatusCode);
    }
}
