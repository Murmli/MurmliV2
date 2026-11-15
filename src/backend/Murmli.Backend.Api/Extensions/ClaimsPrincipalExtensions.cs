using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Murmli.Backend.Api.Extensions;

internal static class ClaimsPrincipalExtensions
{
    public static string GetZitadelUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("The authenticated user has no sub claim, the fallback policy requires it.");
}
