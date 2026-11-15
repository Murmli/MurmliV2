using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Murmli.Backend.Api.Extensions;

namespace Murmli.Backend.Api.Users;

internal static class UserEndpoints
{
    public static async Task<Ok<UserResponse>> GetMeAsync(
        ClaimsPrincipal user,
        UserRegistry userRegistry,
        CancellationToken cancellationToken
    )
    {
        var account = await userRegistry.GetOrCreateAsync(user.GetZitadelUserId(), cancellationToken);
        return TypedResults.Ok(new UserResponse(account.Id));
    }
}
