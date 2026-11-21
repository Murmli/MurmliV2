using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Shopping;

namespace Murmli.Backend.Api.Shopping;

internal static class ShoppingEndpoints
{
    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(25);

    public static async Task<Results<Ok<SyncResponse>, ForbidHttpResult>> SyncAsync(
        SyncRequest request,
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

        return TypedResults.Ok(await shoppingSync.SyncAsync(userId, request, cancellationToken));
    }

    public static async Task<Results<EmptyHttpResult, ForbidHttpResult>> StreamEventsAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        MurmliDbContext context,
        SyncEventHub hub,
        TimeProvider timeProvider
    )
    {
        var cancellationToken = httpContext.RequestAborted;
        if (await context.Users.FindIdAsync(user.GetAuthUserId(), cancellationToken) is not { } userId)
        {
            return TypedResults.Forbid();
        }

        using var subscription = hub.Subscribe(userId);
        using var keepaliveTimer = new PeriodicTimer(KeepaliveInterval, timeProvider);
        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await WriteEventAsync(response, ": connected\n\n", cancellationToken);
            Task<bool>? pokeWait = null;
            Task<bool>? keepaliveWait = null;
            while (true)
            {
                pokeWait ??= subscription.Pokes.WaitToReadAsync(cancellationToken).AsTask();
                keepaliveWait ??= keepaliveTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                if (await Task.WhenAny(pokeWait, keepaliveWait) == pokeWait)
                {
                    await pokeWait;
                    pokeWait = null;
                    while (subscription.Pokes.TryRead(out _)) { }

                    await WriteEventAsync(response, "event: poke\ndata: {}\n\n", cancellationToken);
                }
                else
                {
                    await keepaliveWait;
                    keepaliveWait = null;
                    await WriteEventAsync(response, ": keepalive\n\n", cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TypedResults.Empty;
        }
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
        if (await context.Users.FindIdAsync(user.GetAuthUserId(), cancellationToken) is not { } callerId)
        {
            return TypedResults.Forbid();
        }

        if (!await context.Users.AnyAsync(candidate => candidate.Id == request.UserId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return await context.ExecuteInTransactionAsync<Results<NoContent, ForbidHttpResult, NotFound>>(
            IsolationLevel.ReadCommitted,
            async token =>
            {
                var list = await context.LockListAsync(listId, token);
                var memberIds = await context.GetMemberIdsAsync(listId, token);
                if (list is null || !memberIds.Contains(callerId))
                {
                    return TypedResults.Forbid();
                }

                if (!memberIds.Contains(request.UserId))
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
                    list.Version++;
                    await context.SaveChangesAsync(token);
                    await context.NotifyAsync([.. memberIds, request.UserId], token);
                }

                return TypedResults.NoContent();
            },
            cancellationToken
        );
    }

    public static async Task<Results<NoContent, ForbidHttpResult>> RemoveMemberAsync(
        Guid listId,
        Guid userId,
        ClaimsPrincipal user,
        MurmliDbContext context,
        CancellationToken cancellationToken
    )
    {
        if (await context.Users.FindIdAsync(user.GetAuthUserId(), cancellationToken) is not { } callerId)
        {
            return TypedResults.Forbid();
        }

        return await context.ExecuteInTransactionAsync<Results<NoContent, ForbidHttpResult>>(
            IsolationLevel.ReadCommitted,
            async token =>
            {
                var list = await context.LockListAsync(listId, token);
                var memberIds = await context.GetMemberIdsAsync(listId, token);
                if (list is null || !memberIds.Contains(callerId))
                {
                    return TypedResults.Forbid();
                }

                if (!memberIds.Contains(userId))
                {
                    return TypedResults.NoContent();
                }

                await context
                    .ShoppingListMembers.Where(member => member.ListId == listId && member.UserId == userId)
                    .ExecuteDeleteAsync(token);
                if (memberIds.Count == 1)
                {
                    context.ShoppingLists.Remove(list);
                }
                else
                {
                    list.Version++;
                }

                await context.SaveChangesAsync(token);
                await context.NotifyAsync(memberIds, token);
                return TypedResults.NoContent();
            },
            cancellationToken
        );
    }

    private static async Task WriteEventAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        await response.WriteAsync(text, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
