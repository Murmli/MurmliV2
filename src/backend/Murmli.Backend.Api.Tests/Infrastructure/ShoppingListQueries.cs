using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data.ShoppingLists;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class ShoppingListQueries
{
    public static Task<ShoppingListItem?> ReadItemAsync(ApiHost api, Guid itemId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<ShoppingListItem>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == itemId, TestContext.Current.CancellationToken)
        );

    public static Task<ShoppingList?> ReadListAsync(ApiHost api, Guid listId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<ShoppingList>().AsNoTracking().SingleOrDefaultAsync(list => list.Id == listId, TestContext.Current.CancellationToken)
        );

    public static Task<int> CountItemsAsync(ApiHost api, Guid listId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<ShoppingListItem>().CountAsync(item => item.ScopeId == listId, TestContext.Current.CancellationToken)
        );

    public static Task<List<Guid>> ReadOwnedListIdsAsync(ApiHost api, Guid accountId) =>
        api.QueryDatabaseAsync(context =>
            context
                .Set<SyncScope>()
                .Where(scope => scope.OwnerAccountId == accountId && scope.Kind == SyncTestDevice.ShoppingListScopeKind)
                .OrderBy(scope => scope.Id)
                .Select(scope => scope.Id)
                .ToListAsync(TestContext.Current.CancellationToken)
        );
}
