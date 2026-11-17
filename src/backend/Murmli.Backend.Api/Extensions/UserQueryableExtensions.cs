using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Api.Extensions;

internal static class UserQueryableExtensions
{
    public static Task<Guid?> FindIdAsync(this IQueryable<User> users, string authUserId, CancellationToken cancellationToken = default) =>
        users.Where(user => user.AuthUserId == authUserId).Select(user => (Guid?)user.Id).SingleOrDefaultAsync(cancellationToken);
}
