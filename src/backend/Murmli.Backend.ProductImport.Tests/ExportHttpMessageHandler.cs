using System.Net;

namespace Murmli.Backend.ProductImport.Tests;

internal sealed class ExportHttpMessageHandler(
    Func<Stream> openExport,
    Func<CancellationToken, Task>? onExportRequested = null
) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (onExportRequested is not null)
        {
            await onExportRequested(cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(openExport()) };
    }
}
