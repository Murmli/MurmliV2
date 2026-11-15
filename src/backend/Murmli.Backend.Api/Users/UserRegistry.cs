using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Zitadel;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Identity;
using Npgsql;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserRegistry(
    MurmliDbContext context,
    ZitadelUserClient zitadelUserClient,
    TimeProvider timeProvider,
    ILogger<UserRegistry> logger
)
{
    private static readonly TimeSpan DeletionRecordRetention = TimeSpan.FromHours(24);

    public async Task<User?> GetOrCreateAsync(
        string zitadelUserId,
        DateTimeOffset? issuedAt,
        CancellationToken cancellationToken = default
    )
    {
        if (await FindAsync(zitadelUserId, cancellationToken) is { } existingUser)
        {
            return existingUser;
        }

        if (await IsDeletedAfterIssuanceAsync(zitadelUserId, issuedAt, cancellationToken))
        {
            return null;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            ZitadelUserId = zitadelUserId,
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
            return await FindAsync(zitadelUserId, cancellationToken);
        }

        LogUserCreated(user.Id);
        return user;
    }

    public async Task<bool> DeleteAsync(string zitadelUserId, CancellationToken cancellationToken = default)
    {
        var userId = await context
            .Users.Where(user => user.ZitadelUserId == zitadelUserId)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        context.DeletedUsers.Add(new DeletedUser { ZitadelUserId = zitadelUserId, DeletedAt = now });
        await context.SaveChangesAsync(cancellationToken);

        await context.Users.Where(user => user.ZitadelUserId == zitadelUserId).ExecuteDeleteAsync(cancellationToken);

        await context
            .DeletedUsers.Where(deletedUser => deletedUser.DeletedAt < now - DeletionRecordRetention)
            .ExecuteDeleteAsync(cancellationToken);

        if (!await zitadelUserClient.TryDeleteUserAsync(zitadelUserId, cancellationToken))
        {
            return false;
        }

        LogUserDeleted(userId);
        return true;
    }

    private Task<User?> FindAsync(string zitadelUserId, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(user => user.ZitadelUserId == zitadelUserId, cancellationToken);

    private Task<bool> IsDeletedAfterIssuanceAsync(
        string zitadelUserId,
        DateTimeOffset? issuedAt,
        CancellationToken cancellationToken
    ) =>
        context.DeletedUsers.AnyAsync(
            deletedUser => deletedUser.ZitadelUserId == zitadelUserId && (issuedAt == null || deletedUser.DeletedAt >= issuedAt),
            cancellationToken
        );
}
