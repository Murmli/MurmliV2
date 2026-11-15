using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class TestTokens
{
    public const string Issuer = "https://zitadel.test";

    public const string Audience = "murmli-test-project";

    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(32);

    public static string SigningKeyBase64 { get; } = Convert.ToBase64String(SigningKey);

    public static string Create(string zitadelUserId, string audience = Audience, DateTimeOffset? issuedAt = null)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = audience,
                Claims = new Dictionary<string, object> { ["sub"] = zitadelUserId },
                IssuedAt = issuedAt?.UtcDateTime ?? now,
                NotBefore = now,
                Expires = now.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(SigningKey),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
    }
}
