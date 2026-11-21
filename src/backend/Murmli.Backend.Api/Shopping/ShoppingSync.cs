using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Shopping;

namespace Murmli.Backend.Api.Shopping;

internal sealed class ShoppingSync(MurmliDbContext context, TimeProvider timeProvider)
{
    public async Task<SyncResponse> SyncAsync(Guid userId, SyncRequest request, CancellationToken cancellationToken)
    {
        var results = new List<SyncCommandResult>();
        var discardedTargets = new List<DiscardedTarget>();
        foreach (var command in request.Commands ?? [])
        {
            var result = await context.ExecuteInTransactionAsync(
                IsolationLevel.ReadCommitted,
                token => ProcessAsync(userId, command, token),
                cancellationToken
            );
            results.Add(result);
            // The original verdict of a duplicate is unknown, the command may have been discarded
            if (result.Status is "ignored" or "rejected" or "duplicate" && TargetOf(command) is { } target)
            {
                discardedTargets.Add(target);
            }
        }

        return await context.ExecuteInTransactionAsync(
            IsolationLevel.RepeatableRead,
            token => PullAsync(userId, results, discardedTargets, SyncToken.Decode(request.Token), token),
            cancellationToken
        );
    }

    private async Task<SyncCommandResult> ProcessAsync(Guid userId, SyncCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var inserted = await context.Database.ExecuteSqlAsync(
            $"INSERT INTO shopping.processed_commands (id, user_id, processed_at) VALUES ({command.Id}, {userId}, {now}) ON CONFLICT (id) DO NOTHING",
            cancellationToken
        );
        if (inserted == 0)
        {
            return SyncCommandResult.Duplicate(command.Id);
        }

        if (command.OccurredAt is not { } occurredAt || ParseArguments(command.Arguments) is not { } arguments)
        {
            return SyncCommandResult.Rejected(command.Id, "invalid arguments");
        }

        var change = new Change(command.Id, userId, arguments, occurredAt.ToUniversalTime(), now);
        return command.Type switch
        {
            "createList" => await CreateListAsync(change, cancellationToken),
            "renameList" => await RenameListAsync(change, cancellationToken),
            "addItem" => await AddItemAsync(change, cancellationToken),
            "renameItem" => await ChangeItemAsync(change, ApplyItemName, cancellationToken),
            "setItemChecked" => await ChangeItemAsync(change, ApplyItemChecked, cancellationToken),
            "deleteItem" => await ChangeItemAsync(change, ApplyItemDeletion, cancellationToken),
            _ => SyncCommandResult.Rejected(command.Id, "unknown type"),
        };
    }

    private async Task<SyncCommandResult> CreateListAsync(Change change, CancellationToken cancellationToken)
    {
        if (change.Arguments is not { ListId: { } listId, Name: { Length: > 0 } name })
        {
            return change.Rejected("invalid arguments");
        }

        if (await context.LockListAsync(listId, cancellationToken) is not null)
        {
            return await context.ShoppingListMembers.AnyAsync(
                member => member.ListId == listId && member.UserId == change.UserId,
                cancellationToken
            )
                ? change.Ignored("exists")
                : change.Rejected("not a member");
        }

        context.ShoppingLists.Add(
            new ShoppingList
            {
                Id = listId,
                Name = name,
                NameChangedAt = change.OccurredAt,
                Version = 1,
                CreatedAt = change.Now,
                UpdatedAt = change.Now,
            }
        );
        context.ShoppingListMembers.Add(
            new ShoppingListMember
            {
                Id = Guid.NewGuid(),
                ListId = listId,
                UserId = change.UserId,
                CreatedAt = change.Now,
            }
        );
        await context.SaveChangesAsync(cancellationToken);
        await context.NotifyAsync([change.UserId], cancellationToken);
        return change.Applied();
    }

    private async Task<SyncCommandResult> RenameListAsync(Change change, CancellationToken cancellationToken)
    {
        if (change.Arguments is not { ListId: { } listId, Name: { Length: > 0 } name })
        {
            return change.Rejected("invalid arguments");
        }

        var list = await context.LockListAsync(listId, cancellationToken);
        var memberIds = await context.GetMemberIdsAsync(listId, cancellationToken);
        if (list is null || !memberIds.Contains(change.UserId))
        {
            return change.Rejected("not a member");
        }

        if (change.OccurredAt < list.NameChangedAt)
        {
            return change.Ignored("stale");
        }

        list.Name = name;
        list.NameChangedAt = change.OccurredAt;
        list.UpdatedAt = change.Now;
        list.Version++;
        await context.SaveChangesAsync(cancellationToken);
        await context.NotifyAsync(memberIds, cancellationToken);
        return change.Applied();
    }

    private async Task<SyncCommandResult> AddItemAsync(Change change, CancellationToken cancellationToken)
    {
        if (change.Arguments is not { ItemId: { } itemId, ListId: { } listId, Name: { Length: > 0 } name })
        {
            return change.Rejected("invalid arguments");
        }

        var list = await context.LockListAsync(listId, cancellationToken);
        var memberIds = await context.GetMemberIdsAsync(listId, cancellationToken);
        if (list is null || !memberIds.Contains(change.UserId))
        {
            return change.Rejected("not a member");
        }

        if (await context.ShoppingListItems.FindAsync([itemId], cancellationToken) is { } existingItem)
        {
            return change.Ignored(existingItem.DeletedAt is null ? "exists" : "deleted");
        }

        list.Version++;
        context.ShoppingListItems.Add(
            new ShoppingListItem
            {
                Id = itemId,
                ListId = listId,
                Name = name,
                NameChangedAt = change.OccurredAt,
                Quantity = change.Arguments.Quantity,
                Unit = change.Arguments.Unit,
                Category = change.Arguments.Category,
                Checked = false,
                CheckedChangedAt = change.OccurredAt,
                Version = list.Version,
                CreatedAt = change.Now,
                UpdatedAt = change.Now,
            }
        );
        await context.SaveChangesAsync(cancellationToken);
        await context.NotifyAsync(memberIds, cancellationToken);
        return change.Applied();
    }

    private async Task<SyncCommandResult> ChangeItemAsync(
        Change change,
        Func<ShoppingListItem, Change, SyncCommandResult?> apply,
        CancellationToken cancellationToken
    )
    {
        if (change.Arguments.ItemId is not { } itemId)
        {
            return change.Rejected("invalid arguments");
        }

        var listId = await context
            .ShoppingListItems.Where(item => item.Id == itemId)
            .Select(item => (Guid?)item.ListId)
            .SingleOrDefaultAsync(cancellationToken);
        if (listId is null)
        {
            return change.Ignored("not found");
        }

        var list = await context.LockListAsync(listId.Value, cancellationToken);
        var memberIds = await context.GetMemberIdsAsync(listId.Value, cancellationToken);
        if (list is null || !memberIds.Contains(change.UserId))
        {
            return change.Rejected("not a member");
        }

        var item = await context.ShoppingListItems.SingleAsync(candidate => candidate.Id == itemId, cancellationToken);
        if (item.DeletedAt is not null)
        {
            return change.Ignored("deleted");
        }

        if (apply(item, change) is { } refusal)
        {
            return refusal;
        }

        list.Version++;
        item.Version = list.Version;
        item.UpdatedAt = change.Now;
        await context.SaveChangesAsync(cancellationToken);
        await context.NotifyAsync(memberIds, cancellationToken);
        return change.Applied();
    }

    private static SyncCommandResult? ApplyItemName(ShoppingListItem item, Change change)
    {
        if (change.Arguments.Name is not { Length: > 0 } name)
        {
            return change.Rejected("invalid arguments");
        }

        if (change.OccurredAt < item.NameChangedAt)
        {
            return change.Ignored("stale");
        }

        item.Name = name;
        item.NameChangedAt = change.OccurredAt;
        return null;
    }

    private static SyncCommandResult? ApplyItemChecked(ShoppingListItem item, Change change)
    {
        if (change.Arguments.Checked is not { } isChecked)
        {
            return change.Rejected("invalid arguments");
        }

        if (change.OccurredAt < item.CheckedChangedAt)
        {
            return change.Ignored("stale");
        }

        item.Checked = isChecked;
        item.CheckedChangedAt = change.OccurredAt;
        return null;
    }

    private static SyncCommandResult? ApplyItemDeletion(ShoppingListItem item, Change change)
    {
        item.DeletedAt = change.Now;
        return null;
    }

    private async Task<SyncResponse> PullAsync(
        Guid userId,
        IReadOnlyList<SyncCommandResult> results,
        IReadOnlyList<DiscardedTarget> discardedTargets,
        Dictionary<Guid, long> knownVersions,
        CancellationToken cancellationToken
    )
    {
        var lists = await context
            .ShoppingLists.AsNoTracking()
            .Where(list => context.ShoppingListMembers.Any(member => member.ListId == list.Id && member.UserId == userId))
            .ToListAsync(cancellationToken);
        var membersByList = (
            await context
                .ShoppingListMembers.AsNoTracking()
                .Where(member =>
                    context.ShoppingListMembers.Any(ownMembership => ownMembership.ListId == member.ListId && ownMembership.UserId == userId)
                )
                .ToListAsync(cancellationToken)
        ).ToLookup(member => member.ListId);

        var changedLists = new List<SyncListResponse>();
        var items = new List<SyncItemResponse>();
        foreach (var list in lists)
        {
            var isSnapshot = !knownVersions.TryGetValue(list.Id, out var knownVersion);
            if (!isSnapshot && list.Version <= knownVersion)
            {
                continue;
            }

            var listItems = context.ShoppingListItems.AsNoTracking().Where(item => item.ListId == list.Id);
            listItems = isSnapshot
                ? listItems.Where(item => item.DeletedAt == null)
                : listItems.Where(item => item.Version > knownVersion);
            items.AddRange((await listItems.ToListAsync(cancellationToken)).Select(ToResponse));
            changedLists.Add(ToResponse(list, isSnapshot, membersByList));
        }

        var currentListIds = lists.Select(list => list.Id).ToHashSet();
        var removedListIds = knownVersions.Keys.Where(listId => !currentListIds.Contains(listId)).ToHashSet();
        await AddDiscardedTargetsAsync(discardedTargets, lists, membersByList, currentListIds, changedLists, items, removedListIds, cancellationToken);
        return new SyncResponse(
            results,
            SyncToken.Encode(lists.ToDictionary(list => list.Id, list => list.Version)),
            changedLists,
            items,
            [.. removedListIds]
        );
    }

    private async Task AddDiscardedTargetsAsync(
        IReadOnlyList<DiscardedTarget> discardedTargets,
        List<ShoppingList> lists,
        ILookup<Guid, ShoppingListMember> membersByList,
        HashSet<Guid> currentListIds,
        List<SyncListResponse> changedLists,
        List<SyncItemResponse> items,
        HashSet<Guid> removedListIds,
        CancellationToken cancellationToken
    )
    {
        foreach (var target in discardedTargets.Where(target => target.ListId is not null).DistinctBy(target => (target.ListId, target.ItemId is null)))
        {
            var listId = target.ListId!.Value;
            if (!currentListIds.Contains(listId))
            {
                removedListIds.Add(listId);
            }
            else if (target.ItemId is null && !changedLists.Any(changedList => changedList.Id == listId))
            {
                changedLists.Add(ToResponse(lists.Single(list => list.Id == listId), isSnapshot: false, membersByList));
            }
        }

        var itemTargets = discardedTargets
            .Where(target => target.ItemId is not null)
            .DistinctBy(target => target.ItemId)
            .Where(target => !items.Any(item => item.Id == target.ItemId))
            .ToList();
        var targetItemIds = itemTargets.Select(target => target.ItemId!.Value).ToList();
        var existingItems = await context
            .ShoppingListItems.AsNoTracking()
            .Where(item => targetItemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var target in itemTargets)
        {
            var itemId = target.ItemId!.Value;
            if (!existingItems.TryGetValue(itemId, out var item))
            {
                items.Add(Tombstone(itemId, target.ListId));
            }
            else if (currentListIds.Contains(item.ListId))
            {
                items.Add(ToResponse(item));
            }
            else
            {
                removedListIds.Add(item.ListId);
                items.Add(Tombstone(itemId, item.ListId));
            }
        }
    }

    private static DiscardedTarget? TargetOf(SyncCommand command) =>
        (command.Type, ParseArguments(command.Arguments)) switch
        {
            ("createList" or "renameList", { ListId: { } listId }) => new DiscardedTarget(null, listId),
            ("addItem" or "renameItem" or "setItemChecked" or "deleteItem", { ItemId: { } itemId } arguments) => new DiscardedTarget(
                itemId,
                arguments.ListId
            ),
            _ => null,
        };

    private static SyncListResponse ToResponse(ShoppingList list, bool isSnapshot, ILookup<Guid, ShoppingListMember> membersByList) =>
        new(
            list.Id,
            list.Name,
            list.CreatedAt,
            list.UpdatedAt,
            isSnapshot,
            [.. membersByList[list.Id].Select(member => new SyncListMemberResponse(member.Id, member.UserId, member.CreatedAt))]
        );

    private static SyncItemResponse Tombstone(Guid itemId, Guid? listId) =>
        new(itemId, listId, "", null, null, null, false, default, default, true);

    private static SyncItemResponse ToResponse(ShoppingListItem item) =>
        new(
            item.Id,
            item.ListId,
            item.Name,
            item.Quantity,
            item.Unit,
            item.Category,
            item.Checked,
            item.CreatedAt,
            item.UpdatedAt,
            item.DeletedAt is not null
        );

    private static SyncCommandArguments? ParseArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return arguments.Deserialize<SyncCommandArguments>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Change(Guid CommandId, Guid UserId, SyncCommandArguments Arguments, DateTimeOffset OccurredAt, DateTimeOffset Now)
    {
        public SyncCommandResult Applied() => SyncCommandResult.Applied(CommandId);

        public SyncCommandResult Ignored(string reason) => SyncCommandResult.Ignored(CommandId, reason);

        public SyncCommandResult Rejected(string reason) => SyncCommandResult.Rejected(CommandId, reason);
    }

    private sealed record DiscardedTarget(Guid? ItemId, Guid? ListId);
}
