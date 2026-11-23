using System.Net;
using System.Net.Http.Json;
using System.Text;
using Murmli.Backend.Api.Tests.Infrastructure;
using OpenTelemetry.Metrics;

namespace Murmli.Backend.Api.Tests.Sync;

[Collection<TelemetryCollection>]
public class SyncEndpointsTelemetryTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("noAccount", HttpStatusCode.Gone, "account_gone")]
    [InlineData("missingContentType", HttpStatusCode.UnsupportedMediaType, "invalid_request")]
    [InlineData("nonJsonContentType", HttpStatusCode.UnsupportedMediaType, null)]
    [InlineData("invalidDeviceId", HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData("malformedBody", HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData("nullFrameMember", HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData("tooManyCommands", HttpStatusCode.RequestEntityTooLarge, "request_too_large")]
    [InlineData("bodyTooLarge", HttpStatusCode.RequestEntityTooLarge, "request_too_large")]
    [InlineData("deviceRequestLimit", HttpStatusCode.TooManyRequests, "rate_limited_device")]
    [InlineData("accountRequestLimit", HttpStatusCode.TooManyRequests, "rate_limited_account")]
    [InlineData("accountCommandLimit", HttpStatusCode.TooManyRequests, "rate_limited_commands")]
    [InlineData("requestDeadline", HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    [InlineData("accountDeletedDuringRequest", HttpStatusCode.Gone, "account_gone")]
    [InlineData("lockTimeout", HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    public async Task Sync_RejectedRequest_CountsRejectionReason(string condition, HttpStatusCode expectedStatus, string? expectedRejection)
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var metricSnapshots = new List<MetricSnapshot>();
        int mark;
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                GetRejectionConfiguration(condition),
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            var administration = new DatabaseAdministration(connectionString);
            if (condition == "accountCommandLimit")
            {
                await device.ApplyAllAsync(device.CreateNote(Guid.NewGuid(), now, "note"));
            }

            api.FlushMetrics();
            mark = metricSnapshots.Count;

            using var response = await SendRejectedRequestAsync(condition, api, device, now, administration);

            Assert.Equal(expectedStatus, response.StatusCode);
            api.FlushMetrics();
        }

        Assert.Equal(
            expectedRejection is null ? new Dictionary<string, long>() : new Dictionary<string, long> { [expectedRejection] = 1 },
            TestMetrics.SumByTags(metricSnapshots.Skip(mark), "murmli.sync.requests.rejected", "murmli.sync.rejection.reason")
        );
    }

    private static Dictionary<string, string?> GetRejectionConfiguration(string condition) =>
        condition switch
        {
            "tooManyCommands" => new() { ["Sync:MaxCommandsPerRequest"] = "1" },
            "bodyTooLarge" => new() { ["Sync:MaxRequestBytes"] = "2000" },
            "deviceRequestLimit" => new() { ["Sync:RateLimits:DeviceRequestsPerMinute"] = "1" },
            "accountRequestLimit" => new() { ["Sync:RateLimits:AccountRequestsPerMinute"] = "1" },
            "accountCommandLimit" => new() { ["Sync:MaxCommandsPerRequest"] = "1", ["Sync:RateLimits:AccountCommandsPerHour"] = "1" },
            "requestDeadline" => new() { ["Sync:RequestDeadline"] = "00:00:01", ["Sync:LockTimeout"] = "00:00:05" },
            "lockTimeout" => new() { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
            _ => [],
        };

    private static async Task<HttpResponseMessage> SendRejectedRequestAsync(
        string condition,
        ApiHost api,
        SyncTestDevice device,
        DateTimeOffset now,
        DatabaseAdministration administration
    )
    {
        var deviceId = device.DeviceId.ToString();
        switch (condition)
        {
            case "noAccount":
                return await SyncTestDevice.WithoutAccount(api).SendAsync(JsonContent.Create(device.CreateBody()), Guid.NewGuid().ToString());
            case "missingContentType":
                return await device.SendAsync(null, deviceId);
            case "nonJsonContentType":
                return await device.SendAsync(new StringContent("{}", Encoding.UTF8, "text/plain"), deviceId);
            case "invalidDeviceId":
                return await device.SendAsync(JsonContent.Create(device.CreateBody()), "not-a-device-id");
            case "malformedBody":
                return await device.SendAsync(new StringContent("{", Encoding.UTF8, "application/json"), deviceId);
            case "nullFrameMember":
                return await device.SendAsync(
                    new StringContent("""{"dataTypes":[],"commands":null}""", Encoding.UTF8, "application/json"),
                    deviceId
                );
            case "tooManyCommands":
                return await SendNotesAsync(device, now, count: 2);
            case "bodyTooLarge":
                {
                    var body = device.CreateBody();
                    body["padding"] = new string('x', 3000);
                    return await device.SendAsync(JsonContent.Create(body), deviceId);
                }
            case "deviceRequestLimit":
                return await device.SendAsync(JsonContent.Create(device.CreateBody()), deviceId);
            case "accountRequestLimit":
                {
                    var otherDevice = device.OtherDevice();
                    return await otherDevice.SendAsync(JsonContent.Create(otherDevice.CreateBody()), otherDevice.DeviceId.ToString());
                }
            case "accountCommandLimit":
                return await SendNotesAsync(device, now, count: 1);
            case "requestDeadline" or "lockTimeout":
                {
                    await using var lockedScope = await administration.LockScopeRowAsync(device.PersonalScopeId);
                    return await SendNotesAsync(device, now, count: 1);
                }
            case "accountDeletedDuringRequest":
                {
                    await using var deletion = await administration.BeginUserDeletionAsync(device.AccountId);
                    var sync = SendNotesAsync(device, now, count: 1);
                    await administration.WaitForLockWaitersAsync(1);
                    await deletion.CommitAsync();
                    return await sync;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(condition), condition, "The theory has no request for this condition.");
        }
    }

    private static Task<HttpResponseMessage> SendNotesAsync(SyncTestDevice device, DateTimeOffset now, int count) =>
        device.SendAsync(
            JsonContent.Create(device.CreateBody([.. Enumerable.Range(0, count).Select(_ => device.CreateNote(Guid.NewGuid(), now, "note"))])),
            device.DeviceId.ToString()
        );
}
