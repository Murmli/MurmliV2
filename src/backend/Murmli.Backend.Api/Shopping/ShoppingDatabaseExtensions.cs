using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Shopping;

namespace Murmli.Backend.Api.Shopping;

internal static class ShoppingDatabaseExtensions
{
    public static Task<TResult> ExecuteInTransactionAsync<TResult>(
        this MurmliDbContext context,
        IsolationLevel isolationLevel,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    ) =>
        // The retrying execution strategy refuses user-initiated transactions outside of ExecuteAsync
        context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });

    public static async Task<ShoppingList?> LockListAsync(this MurmliDbContext context, Guid listId, CancellationToken cancellationToken) =>
        (await context.ShoppingLists.FromSql($"SELECT * FROM shopping.lists WHERE id = {listId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    public static Task<List<Guid>> GetMemberIdsAsync(this MurmliDbContext context, Guid listId, CancellationToken cancellationToken) =>
        context.ShoppingListMembers.Where(member => member.ListId == listId).Select(member => member.UserId).ToListAsync(cancellationToken);

    public static async Task NotifyAsync(this MurmliDbContext context, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new ShoppingChangeNotification(userIds), JsonSerializerOptions.Web);
        await context.Database.ExecuteSqlAsync($"SELECT pg_notify({ShoppingChangeNotification.Channel}, {payload})", cancellationToken);
    }
}
