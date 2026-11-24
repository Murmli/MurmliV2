using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed class SyncLoadDevice
{
    private const string DeviceIdHeader = "Murmli-Device-Id";

    private readonly HttpClient httpClient;

    private readonly string authUserId;

    private readonly Guid deviceId = Guid.NewGuid();

    private SyncLoadDevice(HttpClient httpClient, string authUserId)
    {
        this.httpClient = httpClient;
        this.authUserId = authUserId;
    }

    public static async Task<SyncLoadDevice> SignInAsync(HttpClient httpClient)
    {
        var authUserId = SyncTestDevice.CreateAuthUserId();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(authUserId));
        using var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new SyncLoadDevice(httpClient, authUserId);
    }

    public SyncLoadDevice OtherDevice() => new(httpClient, authUserId);

    public async Task<SyncLoadExchange> SyncAsync(string? token, IReadOnlyList<JsonObject> commands, bool acceptGzip = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(SyncTestCommands.CreateBody(token, SyncTestDevice.ShoppingDataTypes, commands)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/sync") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(authUserId));
        request.Headers.Add(DeviceIdHeader, deviceId.ToString());
        if (acceptGzip)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        }

        var started = Stopwatch.GetTimestamp();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var wireBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var duration = Stopwatch.GetElapsedTime(started);
        var compressed = response.Content.Headers.ContentEncoding.Contains("gzip");
        return new SyncLoadExchange(response.StatusCode, duration, wireBody.Length, compressed ? Decompress(wireBody) : wireBody, compressed);
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var decompression = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        decompression.CopyTo(decompressed);
        return decompressed.ToArray();
    }
}
