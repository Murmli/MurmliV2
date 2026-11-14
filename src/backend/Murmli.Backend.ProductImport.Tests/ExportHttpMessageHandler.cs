using System.Net;

namespace Murmli.Backend.ProductImport.Tests;

internal sealed class ExportHttpMessageHandler(byte[] export) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(export) });
}
