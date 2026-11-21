using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Shopping;

namespace Murmli.Backend.Api.Shopping;

internal sealed class ShoppingSync(MurmliDbContext context, TimeProvider timeProvider)
{
    private const string ListsTable = "lists";
    private const string ItemsTable = "items";

    public Task<IReadOnlyList<SyncOperationResult>> ApplyAsync(
        Guid userId,
        IReadOnlyList<SyncOperation> operations,
        CancellationToken cancellationToken
    ) =>
        // The retrying execution strategy refuses user-initiated transactions outside of ExecuteAsync
        context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                var results = new List<SyncOperationResult>(operations.Count);
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                foreach (var operation in operations)
                {
                    results.Add(await ApplyAsync(userId, operation, cancellationToken));
                }

                await transaction.CommitAsync(cancellationToken);
                return (IReadOnlyList<SyncOperationResult>)results;
            });

    private async Task<SyncOperationResult> ApplyAsync(Guid userId, SyncOperation operation, CancellationToken cancellationToken)
    {
        if (operation.Type is not (ListsTable or ItemsTable))
        {
            return SyncOperationResult.Rejected(operation, "not writable via sync");
        }

        if (!Guid.TryParse(operation.Id, out var id))
        {
            return SyncOperationResult.Rejected(operation, "id must be a uuid");
        }

        var data = new SyncData(operation.Data);
        try
        {
            var result = (operation.Type, operation.Operation) switch
            {
                (ListsTable, "PUT") => await PutListAsync(userId, id, operation, data, cancellationToken),
                (ListsTable, "PATCH") => await PatchListAsync(userId, id, operation, data, cancellationToken),
                (ListsTable, "DELETE") => await DeleteListAsync(userId, id, operation, cancellationToken),
                (ItemsTable, "PUT") => await PutItemAsync(userId, id, operation, data, cancellationToken),
                (ItemsTable, "PATCH") => await PatchItemAsync(userId, id, operation, data, cancellationToken),
                (ItemsTable, "DELETE") => await DeleteItemAsync(userId, id, operation, cancellationToken),
                _ => SyncOperationResult.Rejected(operation, "unknown op"),
            };
            await context.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (SyncValueException exception)
        {
            context.ChangeTracker.Clear();
            return SyncOperationResult.Rejected(operation, exception.Message);
        }
    }

    private async Task<SyncOperationResult> PutListAsync(
        Guid userId,
        Guid id,
        SyncOperation operation,
        SyncData data,
        CancellationToken cancellationToken
    )
    {
        var list = await context.ShoppingLists.FindAsync([id], cancellationToken);
        if (list is not null)
        {
            return await PatchListAsync(userId, list, operation, data, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var createdAt = data.FindTimestamp("created_at") ?? now;
        context.ShoppingLists.Add(
            new ShoppingList
            {
                Id = id,
                Name = data.GetRequiredString("name"),
                CreatedAt = createdAt,
                UpdatedAt = data.FindTimestamp("updated_at") ?? createdAt,
            }
        );
        context.ShoppingListMembers.Add(
            new ShoppingListMember
            {
                Id = Guid.NewGuid(),
                ListId = id,
                UserId = userId,
                CreatedAt = now,
            }
        );
        return SyncOperationResult.Applied(operation);
    }

    private async Task<SyncOperationResult> PatchListAsync(
        Guid userId,
        Guid id,
        SyncOperation operation,
        SyncData data,
        CancellationToken cancellationToken
    ) =>
        await context.ShoppingLists.FindAsync([id], cancellationToken) is { } list
            ? await PatchListAsync(userId, list, operation, data, cancellationToken)
            : SyncOperationResult.Ignored(operation, "not found");

    private async Task<SyncOperationResult> PatchListAsync(
        Guid userId,
        ShoppingList list,
        SyncOperation operation,
        SyncData data,
        CancellationToken cancellationToken
    )
    {
        if (!await IsMemberAsync(userId, list.Id, cancellationToken))
        {
            return SyncOperationResult.Rejected(operation, "not a member");
        }

        var updatedAt = data.GetRequiredTimestamp("updated_at");
        if (updatedAt < list.UpdatedAt)
        {
            return SyncOperationResult.Ignored(operation, "stale");
        }

        if (data.Contains("name"))
        {
            list.Name = data.GetRequiredString("name");
        }

        list.UpdatedAt = updatedAt;
        return SyncOperationResult.Applied(operation);
    }

    private async Task<SyncOperationResult> DeleteListAsync(Guid userId, Guid id, SyncOperation operation, CancellationToken cancellationToken)
    {
        var list = await context.ShoppingLists.FindAsync([id], cancellationToken);
        if (list is null)
        {
            return SyncOperationResult.Ignored(operation, "not found");
        }

        if (!await IsMemberAsync(userId, id, cancellationToken))
        {
            return SyncOperationResult.Rejected(operation, "not a member");
        }

        context.ShoppingLists.Remove(list);
        return SyncOperationResult.Applied(operation);
    }

    private async Task<SyncOperationResult> PutItemAsync(
        Guid userId,
        Guid id,
        SyncOperation operation,
        SyncData data,
        CancellationToken cancellationToken
    )
    {
        var item = await context.ShoppingListItems.FindAsync([id], cancellationToken);
        if (item is not null)
        {
            return await PatchItemAsync(userId, item, operation, data, cancellationToken);
        }

        var listId = data.GetRequiredGuid("list_id");
        if (!await IsMemberAsync(userId, listId, cancellationToken))
        {
            return SyncOperationResult.Rejected(operation, "not a member");
        }

        var createdAt = data.FindTimestamp("created_at") ?? timeProvider.GetUtcNow();
        context.ShoppingListItems.Add(
            new ShoppingListItem
            {
                Id = id,
                ListId = listId,
                Name = data.GetRequiredString("name"),
                Quantity = data.FindDouble("quantity"),
                Unit = data.FindString("unit"),
                Category = data.FindString("category"),
                Checked = data.FindBoolean("checked") ?? false,
                CreatedAt = createdAt,
                UpdatedAt = data.FindTimestamp("updated_at") ?? createdAt,
            }
        );
        return SyncOperationResult.Applied(operation);
    }

    private async Task<SyncOperationResult> PatchItemAsync(
        Guid userId,
        Guid id,
        SyncOperation operation,
        SyncData data,
        CancellationToken cancellationToken
    ) =>
        await context.ShoppingListItems.FindAsync([id], cancellationToken) is { } item
            ? await PatchItemAsync(userId, item, operation, data, cancellationToken)
            : SyncOperationResult.Ignored(operation, "not found");

    private async Task<SyncOperationResult> PatchItemAsync(
        Guid userId,
        ShoppingListItem item,
        SyncOperation operation,
        SyncData data,
        CancellationToken cancellationToken
    )
    {
        if (!await IsMemberAsync(userId, item.ListId, cancellationToken))
        {
            return SyncOperationResult.Rejected(operation, "not a member");
        }

        var updatedAt = data.GetRequiredTimestamp("updated_at");
        if (updatedAt < item.UpdatedAt)
        {
            return SyncOperationResult.Ignored(operation, "stale");
        }

        if (data.Contains("name"))
        {
            item.Name = data.GetRequiredString("name");
        }

        if (data.Contains("quantity"))
        {
            item.Quantity = data.FindDouble("quantity");
        }

        if (data.Contains("unit"))
        {
            item.Unit = data.FindString("unit");
        }

        if (data.Contains("category"))
        {
            item.Category = data.FindString("category");
        }

        if (data.Contains("checked"))
        {
            item.Checked = data.FindBoolean("checked") ?? throw new SyncValueException("checked must not be null");
        }

        item.UpdatedAt = updatedAt;
        return SyncOperationResult.Applied(operation);
    }

    private async Task<SyncOperationResult> DeleteItemAsync(Guid userId, Guid id, SyncOperation operation, CancellationToken cancellationToken)
    {
        var item = await context.ShoppingListItems.FindAsync([id], cancellationToken);
        if (item is null)
        {
            return SyncOperationResult.Ignored(operation, "not found");
        }

        if (!await IsMemberAsync(userId, item.ListId, cancellationToken))
        {
            return SyncOperationResult.Rejected(operation, "not a member");
        }

        context.ShoppingListItems.Remove(item);
        return SyncOperationResult.Applied(operation);
    }

    private Task<bool> IsMemberAsync(Guid userId, Guid listId, CancellationToken cancellationToken) =>
        context.ShoppingListMembers.AnyAsync(member => member.ListId == listId && member.UserId == userId, cancellationToken);
}
