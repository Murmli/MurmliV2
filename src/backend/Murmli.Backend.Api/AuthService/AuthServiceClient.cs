using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Murmli.Backend.Api.AuthService;

internal sealed partial class AuthServiceClient(HttpClient httpClient, ILogger<AuthServiceClient> logger)
{
    public async Task<bool> TryDeleteUserAsync(string authUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("service/delete-user", new DeleteUserRequest(authUserId), cancellationToken);
            if (response.IsSuccessStatusCode)
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
