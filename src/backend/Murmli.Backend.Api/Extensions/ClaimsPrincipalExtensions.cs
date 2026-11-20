using System.Globalization;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Murmli.Backend.Api.Extensions;

internal static class ClaimsPrincipalExtensions
{
    public static string? FindAuthUserId(this ClaimsPrincipal user) => user.FindFirstValue(JwtRegisteredClaimNames.Sub);

    public static string GetAuthUserId(this ClaimsPrincipal user) =>
        user.FindAuthUserId() ?? throw new InvalidOperationException("The authenticated user has no sub claim, the default and fallback policies require it.");

    public static DateTimeOffset? GetIssuedAt(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Iat), CultureInfo.InvariantCulture, out var unixSeconds)
        && unixSeconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds()
        && unixSeconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null;
}
