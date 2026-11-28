using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Murmli.Backend.Api.Extensions;

namespace Murmli.Backend.Api.Users;

internal static class UserEndpoints
{
    public static async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> GetMeAsync(
        ClaimsPrincipal user,
        UserRegistry userRegistry,
        CancellationToken cancellationToken
    )
    {
        var authUserId = user.GetAuthUserId();
        var account = await userRegistry.GetOrCreateAsync(authUserId, user.GetIssuedAt(), cancellationToken);
        if (account is null)
        {
            return TypedResults.Unauthorized();
        }

        if (account.DeletionStartedAt is not null)
        {
            // Signing in finishes a deletion that stopped after the marker, like a repeated DELETE
            await userRegistry.CompleteDeletionAsync(authUserId, CancellationToken.None);
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new UserResponse(account.Id));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> DeleteMeAsync(ClaimsPrincipal user, UserRegistry userRegistry)
    {
        // The app may close right after the tap, a started deletion must still run to the end
        if (await userRegistry.DeleteAsync(user.GetAuthUserId(), CancellationToken.None))
        {
            return TypedResults.NoContent();
        }

        return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway);
    }
}
