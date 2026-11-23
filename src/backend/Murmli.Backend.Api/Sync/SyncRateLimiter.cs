using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Murmli.Backend.Api.Diagnostics;
using Murmli.Backend.Sync.Configuration;

namespace Murmli.Backend.Api.Sync;

internal sealed class SyncRateLimiter : IDisposable
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    // Keyed by a string: devices choose their ids and the Guid hash has no seed, while Dictionary switches string keys to randomized hashing on collisions
    private readonly PartitionedRateLimiter<string> deviceRequests;

    private readonly PartitionedRateLimiter<Guid> accountRequests;

    private readonly PartitionedRateLimiter<Guid> accountCommands;

    public SyncRateLimiter(IOptions<SyncOptions> options)
    {
        var rateLimits = options.Value.RateLimits;
        deviceRequests = CreateFixedWindowLimiter<string>(rateLimits.DeviceRequestsPerMinute);
        accountRequests = CreateFixedWindowLimiter<Guid>(rateLimits.AccountRequestsPerMinute);
        var commandBucket = new TokenBucketRateLimiterOptions
        {
            TokenLimit = rateLimits.AccountCommandsPerHour,
            ReplenishmentPeriod = Minute,
            TokensPerPeriod = Math.Max(1, rateLimits.AccountCommandsPerHour / 60),
        };
        accountCommands = PartitionedRateLimiter.Create<Guid, Guid>(accountId =>
            RateLimitPartition.GetTokenBucketLimiter(accountId, _ => commandBucket)
        );
    }

    public bool TryAcquireRequest(Guid accountId, Guid deviceId, out TimeSpan retryAfter, [NotNullWhen(false)] out string? rejection)
    {
        rejection =
            !TryAcquire(accountRequests, accountId, permitCount: 0, out retryAfter) ? SyncRequestRejection.RateLimitedAccount
            : !TryAcquire(deviceRequests, $"{accountId:N}{deviceId:N}", permitCount: 1, out retryAfter) ? SyncRequestRejection.RateLimitedDevice
            : !TryAcquire(accountRequests, accountId, permitCount: 1, out retryAfter) ? SyncRequestRejection.RateLimitedAccount
            : null;
        return rejection is null;
    }

    public bool TryAcquireCommands(Guid accountId, int commandCount, out TimeSpan retryAfter) =>
        TryAcquire(accountCommands, accountId, commandCount, out retryAfter);

    public void Dispose()
    {
        deviceRequests.Dispose();
        accountRequests.Dispose();
        accountCommands.Dispose();
    }

    private static PartitionedRateLimiter<TKey> CreateFixedWindowLimiter<TKey>(int permitsPerMinute)
        where TKey : notnull
    {
        var window = new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = Minute };
        return PartitionedRateLimiter.Create<TKey, TKey>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => window));
    }

    private static bool TryAcquire<TResource>(
        PartitionedRateLimiter<TResource> limiter,
        TResource resource,
        int permitCount,
        out TimeSpan retryAfter
    )
    {
        using var lease = limiter.AttemptAcquire(resource, permitCount);
        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter) ? leaseRetryAfter : TimeSpan.Zero;
        return lease.IsAcquired;
    }
}
