using System.Net;
using Microsoft.Extensions.Logging;

namespace Murmli.Backend.Api.Zitadel;

internal sealed partial class ZitadelUserClient(HttpClient httpClient, ILogger<ZitadelUserClient> logger)
{
    public async Task<bool> TryDeleteUserAsync(string zitadelUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.DeleteAsync($"v2/users/{Uri.EscapeDataString(zitadelUserId)}", cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return true;
            }

            LogUserDeletionFailed((int)response.StatusCode);
            return false;
        }
        catch (HttpRequestException exception)
        {
            LogUserDeletionFailed(null, exception);
            return false;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUserDeletionFailed(null, exception);
            return false;
        }
    }
}
