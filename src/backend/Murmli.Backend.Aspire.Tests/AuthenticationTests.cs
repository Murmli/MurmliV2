using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Murmli.Backend.Aspire.Tests.Infrastructure;

namespace Murmli.Backend.Aspire.Tests;

[Collection<AppHostCollection>]
public sealed class AuthenticationTests(AppHostFixture appHost) : IDisposable
{
    // Cookies would make Better Auth check the origin of later POST requests
    private readonly HttpClient authClient = new(new SocketsHttpHandler { UseCookies = false })
    {
        BaseAddress = appHost.App.GetEndpoint("auth", "http"),
    };

    public void Dispose() => authClient.Dispose();

    [Fact(Timeout = 60_000)]
    public async Task SignUp_Disabled_IsRejected()
    {
        using var response = await authClient.PostAsJsonAsync(
            "/api/auth/sign-up/email",
            new { email = "new-user@murmli.de", password = "new-user-password", name = "New User" },
            TestContext.Current.CancellationToken
        );

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact(Timeout = 60_000)]
    public async Task DeleteUser_WithoutServiceKey_ReturnsUnauthorized()
    {
        using var response = await authClient.PostAsJsonAsync(
            "/service/delete-user",
            new { userId = "anything" },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 60_000)]
    public async Task SignInWithApple_InvalidIdentityToken_IsRejected()
    {
        using var response = await authClient.PostAsJsonAsync(
            "/api/auth/sign-in/social",
            new { provider = "apple", idToken = new { token = "not-a-jwt" } },
            TestContext.Current.CancellationToken
        );

        // 401 instead of 404 shows the provider is configured, a non-JWT token fails before Better Auth fetches Apple's keys
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 60_000)]
    public async Task SignInWithGoogle_InvalidIdToken_IsRejected()
    {
        using var response = await authClient.PostAsJsonAsync(
            "/api/auth/sign-in/social",
            new { provider = "google", idToken = new { token = "not-a-jwt" } },
            TestContext.Current.CancellationToken
        );

        // 401 instead of 404 shows the provider is configured, a non-JWT token fails before Better Auth fetches Google's keys
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Timeout = 60_000)]
    public async Task SeedUser_SignInAndDeleteAccount_RemovesUserEverywhere()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var signInResponse = await SignInSeedUserAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, signInResponse.StatusCode);
        var sessionToken = Assert.Single(signInResponse.Headers.GetValues("set-auth-token"));

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/token");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        using var tokenResponse = await authClient.SendAsync(tokenRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var accessToken = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("token").GetString()!;

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(accessToken.Split('.')[1]));
        Assert.Equal(["aud", "exp", "iat", "iss", "sub"], payload.RootElement.EnumerateObject().Select(claim => claim.Name).Order());

        using var apiClient = appHost.App.CreateHttpClient("api");
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var getMeResponse = await apiClient.GetAsync("/v1/users/me", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getMeResponse.StatusCode);

        using var deleteMeResponse = await apiClient.DeleteAsync("/v1/users/me", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteMeResponse.StatusCode);

        using var getMeAfterDeletionResponse = await apiClient.GetAsync("/v1/users/me", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, getMeAfterDeletionResponse.StatusCode);

        using var signInAfterDeletionResponse = await SignInSeedUserAsync(cancellationToken);
        Assert.False(signInAfterDeletionResponse.IsSuccessStatusCode);
    }

    private Task<HttpResponseMessage> SignInSeedUserAsync(CancellationToken cancellationToken) =>
        authClient.PostAsJsonAsync("/api/auth/sign-in/email", new { email = "test@murmli.de", password = "password" }, cancellationToken);
}
