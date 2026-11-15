using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Identity;
using Npgsql;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserRegistry(MurmliDbContext context, TimeProvider timeProvider, ILogger<UserRegistry> logger)
{
    public async Task<User> GetOrCreateAsync(string zitadelUserId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(zitadelUserId, cancellationToken) is { } existingUser)
        {
            return existingUser;
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
            // A concurrent first request of the same user inserted the row in the meantime
            context.ChangeTracker.Clear();
            return await FindAsync(zitadelUserId, cancellationToken)
                ?? throw new InvalidOperationException("The user vanished after a concurrent insert.", exception);
        }

        LogUserCreated(user.Id);
        return user;
    }

    private Task<User?> FindAsync(string zitadelUserId, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(user => user.ZitadelUserId == zitadelUserId, cancellationToken);
}
