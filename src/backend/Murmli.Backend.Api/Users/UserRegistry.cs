using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.AuthService;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Identity;
using Murmli.Backend.Sync.Accounts;
using Npgsql;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserRegistry(
    MurmliDbContext context,
    AuthServiceClient authServiceClient,
    SyncAccountLifecycle syncAccountLifecycle,
    TimeProvider timeProvider,
    ILogger<UserRegistry> logger
)
{
    private static readonly TimeSpan DeletionRecordRetention = TimeSpan.FromHours(24);

    public async Task<User?> GetOrCreateAsync(
        string authUserId,
        DateTimeOffset? issuedAt,
        CancellationToken cancellationToken = default
    )
    {
        if (await FindAsync(authUserId, cancellationToken) is { } existingUser)
        {
            return existingUser;
        }

        if (await IsDeletedAfterIssuanceAsync(authUserId, issuedAt, cancellationToken))
        {
            return null;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            AuthUserId = authUserId,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        context.Users.Add(user);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent first request of the same user inserted the row in the meantime, a concurrent deletion may have removed it again
            context.ChangeTracker.Clear();
            return await FindAsync(authUserId, cancellationToken);
        }

        LogUserCreated(user.Id);
        return user;
    }

    public async Task<bool> DeleteAsync(string authUserId, CancellationToken cancellationToken = default)
    {
        var userId = await context.Users.FindIdAsync(authUserId, cancellationToken);
        await AddDeletionRecordAsync(authUserId, cancellationToken);
        if (userId is { } accountId)
        {
            await syncAccountLifecycle.MarkDeletingAsync(accountId, cancellationToken);
        }

        return await CompleteDeletionAsync(authUserId, cancellationToken);
    }

    public async Task<bool> CompleteDeletionAsync(string authUserId, CancellationToken cancellationToken = default)
    {
        var userId = await context.Users.FindIdAsync(authUserId, cancellationToken);
        if (userId is { } accountId)
        {
            await syncAccountLifecycle.DeleteOwnedScopesAsync(accountId, cancellationToken);
        }

        if (!await authServiceClient.TryDeleteUserAsync(authUserId, cancellationToken))
        {
            return false;
        }

        // Written after the deletion in auth, so no token the user can still present is newer than this record
        var deletedAt = await AddDeletionRecordAsync(authUserId, cancellationToken);
        await context.Users.Where(user => user.AuthUserId == authUserId).ExecuteDeleteAsync(cancellationToken);
        await context
            .DeletedUsers.Where(deletedUser => deletedUser.DeletedAt < deletedAt - DeletionRecordRetention)
            .ExecuteDeleteAsync(cancellationToken);

        LogUserDeleted(userId);
        return true;
    }

    private async Task<DateTimeOffset> AddDeletionRecordAsync(string authUserId, CancellationToken cancellationToken)
    {
        var deletedAt = timeProvider.GetUtcNow();
        context.DeletedUsers.Add(new DeletedUser { AuthUserId = authUserId, DeletedAt = deletedAt });
        await context.SaveChangesAsync(cancellationToken);
        return deletedAt;
    }

    private Task<User?> FindAsync(string authUserId, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(user => user.AuthUserId == authUserId, cancellationToken);

    private Task<bool> IsDeletedAfterIssuanceAsync(
        string authUserId,
        DateTimeOffset? issuedAt,
        CancellationToken cancellationToken
    ) =>
        context.DeletedUsers.AnyAsync(
            deletedUser => deletedUser.AuthUserId == authUserId && (issuedAt == null || deletedUser.DeletedAt >= issuedAt),
            cancellationToken
        );
}
