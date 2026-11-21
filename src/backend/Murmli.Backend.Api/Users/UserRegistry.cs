using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.AuthService;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Api.Shopping;
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
        var userId = await context.Users.FindIdAsync(authUserId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        context.DeletedUsers.Add(new DeletedUser { AuthUserId = authUserId, DeletedAt = now });
        await context.SaveChangesAsync(cancellationToken);

        if (userId is { } existingUserId)
        {
            await LeaveShoppingListsAsync(existingUserId, cancellationToken);
        }

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

    private Task<bool> LeaveShoppingListsAsync(Guid userId, CancellationToken cancellationToken) =>
        context.ExecuteInTransactionAsync(
            IsolationLevel.ReadCommitted,
            async token =>
            {
                var listIds = await context
                    .ShoppingListMembers.Where(member => member.UserId == userId)
                    .Select(member => member.ListId)
                    .OrderBy(listId => listId)
                    .ToListAsync(token);
                foreach (var listId in listIds)
                {
                    if (await context.LockListAsync(listId, token) is not { } list)
                    {
                        continue;
                    }

                    var remainingMemberIds = (await context.GetMemberIdsAsync(listId, token)).Where(memberId => memberId != userId).ToList();
                    if (remainingMemberIds.Count == 0)
                    {
                        context.ShoppingLists.Remove(list);
                    }
                    else
                    {
                        await context
                            .ShoppingListMembers.Where(member => member.ListId == listId && member.UserId == userId)
                            .ExecuteDeleteAsync(token);
                        list.Version++;
                        await context.NotifyAsync(remainingMemberIds, token);
                    }

                    await context.SaveChangesAsync(token);
                }

                return true;
            },
            cancellationToken
        );

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
