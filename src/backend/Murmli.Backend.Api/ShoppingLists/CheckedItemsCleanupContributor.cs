using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Data.ShoppingLists;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Writing;

namespace Murmli.Backend.Api.ShoppingLists;

internal sealed class CheckedItemsCleanupContributor(
    MurmliDbContext context,
    SyncServerWriter writer,
    IOptions<ShoppingListOptions> options
) : ISyncCleanupContributor
{
    public string Name => "checked_shopping_list_items";

    public async Task RunAsync(SyncCleanupContext cleanupContext, CancellationToken cancellationToken = default)
    {
        var arrivedBefore = cleanupContext.Now - options.Value.CheckedItemRetention;
        var skippedItemIds = new List<Guid>();
        var skippedListIds = new List<Guid>();
        while (true)
        {
            var items = await context
                .Set<ShoppingListItem>()
                .Where(item =>
                    item.CheckedArrivedAt < arrivedBefore
                    && !skippedItemIds.Contains(item.Id)
                    && !skippedListIds.Contains(item.ScopeId)
                )
                .OrderBy(item => item.CheckedArrivedAt)
                .ThenBy(item => item.Id)
                .Take(cleanupContext.BatchSize)
                .Select(item => new { item.Id, item.ScopeId, item.CheckedArrivedAt })
                .ToListAsync(cancellationToken);
            if (items.Count == 0)
            {
                return;
            }

            foreach (var item in items)
            {
                if (skippedListIds.Contains(item.ScopeId))
                {
                    continue;
                }

                try
                {
                    if (await TryRemoveAsync(item.ScopeId, item.Id, item.CheckedArrivedAt, cancellationToken))
                    {
                        cleanupContext.AddRemovedRecords(1);
                    }
                    else
                    {
                        skippedItemIds.Add(item.Id);
                    }
                }
                catch (Exception exception) when (exception.IsLockTimeout())
                {
                    skippedListIds.Add(item.ScopeId);
                }
            }
        }
    }

    private async Task<bool> TryRemoveAsync(Guid listId, Guid itemId, DateTimeOffset? arrivedAt, CancellationToken cancellationToken)
    {
        var result = await writer.WriteAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.System,
                SyncCommandKind.Delete,
                listId,
                ShoppingListItemType.DataTypeName,
                itemId,
                Precondition: record => record is ShoppingListItem { Checked: true } checkedItem && checkedItem.CheckedArrivedAt == arrivedAt
            ),
            cancellationToken
        );
        return result.Status == SyncResultStatus.Applied.Key;
    }
}
