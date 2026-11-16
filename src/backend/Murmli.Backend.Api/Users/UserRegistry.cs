using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.AuthService;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Identity;
using Npgsql;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserRegistry(
    MurmliDbContext context,
    AuthServiceClient authServiceClient,
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
        var userId = await context
            .Users.Where(user => user.AuthUserId == authUserId)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        context.DeletedUsers.Add(new DeletedUser { AuthUserId = authUserId, DeletedAt = now });
        await context.SaveChangesAsync(cancellationToken);

        await context.Users.Where(user => user.AuthUserId == authUserId).ExecuteDeleteAsync(cancellationToken);

        await context
            .DeletedUsers.Where(deletedUser => deletedUser.DeletedAt < now - DeletionRecordRetention)
            .ExecuteDeleteAsync(cancellationToken);

        if (!await authServiceClient.TryDeleteUserAsync(authUserId, cancellationToken))
        {
            return false;
        }

        LogUserDeleted(userId);
        return true;
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
