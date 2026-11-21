using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Shopping;

namespace Murmli.Backend.Api.Shopping;

internal static class ShoppingEndpoints
{
    public static async Task<Results<Ok<SyncUploadResponse>, ForbidHttpResult>> UploadAsync(
        SyncUploadRequest request,
        ClaimsPrincipal user,
        MurmliDbContext context,
        ShoppingSync shoppingSync,
        CancellationToken cancellationToken
    )
    {
        if (await context.Users.FindIdAsync(user.GetAuthUserId(), cancellationToken) is not { } userId)
        {
            return TypedResults.Forbid();
        }

        var results = await shoppingSync.ApplyAsync(userId, request.Operations ?? [], cancellationToken);
        return TypedResults.Ok(new SyncUploadResponse(results));
    }

    public static async Task<Results<NoContent, ForbidHttpResult, NotFound>> AddMemberAsync(
        Guid listId,
        AddListMemberRequest request,
        ClaimsPrincipal user,
        MurmliDbContext context,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        if (!await IsCallerMemberAsync(context, user, listId, cancellationToken))
        {
            return TypedResults.Forbid();
        }

        if (!await context.Users.AnyAsync(candidate => candidate.Id == request.UserId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (!await context.ShoppingListMembers.AnyAsync(member => member.ListId == listId && member.UserId == request.UserId, cancellationToken))
        {
            context.ShoppingListMembers.Add(
                new ShoppingListMember
                {
                    Id = Guid.NewGuid(),
                    ListId = listId,
                    UserId = request.UserId,
                    CreatedAt = timeProvider.GetUtcNow(),
                }
            );
            await context.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, ForbidHttpResult>> RemoveMemberAsync(
        Guid listId,
        Guid userId,
        ClaimsPrincipal user,
        MurmliDbContext context,
        CancellationToken cancellationToken
    )
    {
        if (!await IsCallerMemberAsync(context, user, listId, cancellationToken))
        {
            return TypedResults.Forbid();
        }

        await context.ShoppingListMembers.Where(member => member.ListId == listId && member.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context
            .ShoppingLists.Where(list => list.Id == listId && !context.ShoppingListMembers.Any(member => member.ListId == list.Id))
            .ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<bool> IsCallerMemberAsync(
        MurmliDbContext context,
        ClaimsPrincipal user,
        Guid listId,
        CancellationToken cancellationToken
    ) =>
        await context.Users.FindIdAsync(user.GetAuthUserId(), cancellationToken) is { } callerId
        && await context.ShoppingListMembers.AnyAsync(member => member.ListId == listId && member.UserId == callerId, cancellationToken);
}
