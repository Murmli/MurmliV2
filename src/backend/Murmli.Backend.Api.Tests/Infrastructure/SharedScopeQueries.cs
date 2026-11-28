using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class SharedScopeQueries
{
    public static Task<SyncScope?> ReadScopeAsync(ApiHost api, Guid scopeId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<SyncScope>().AsNoTracking().SingleOrDefaultAsync(scope => scope.Id == scopeId, TestContext.Current.CancellationToken)
        );

    public static Task<List<SyncMemberEntry>> ReadMemberEntriesAsync(ApiHost api, Guid scopeId) =>
        api.QueryDatabaseAsync(context =>
            context
                .Set<SyncMemberEntry>()
                .AsNoTracking()
                .Where(entry => entry.ScopeId == scopeId)
                .OrderBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.Id)
                .ToListAsync(TestContext.Current.CancellationToken)
        );

    public static Task<string?> ReadBoardTitleAsync(ApiHost api, Guid boardId) =>
        api.QueryDatabaseAsync(context =>
            context
                .Set<TestBoard>()
                .Where(board => board.Id == boardId)
                .Select(board => board.Title)
                .SingleOrDefaultAsync(TestContext.Current.CancellationToken)
        );

    public static Task<TestCard?> ReadCardAsync(ApiHost api, Guid cardId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<TestCard>().AsNoTracking().SingleOrDefaultAsync(card => card.Id == cardId, TestContext.Current.CancellationToken)
        );

    public static Task<int> CountCardsAsync(ApiHost api, Guid scopeId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<TestCard>().CountAsync(card => card.ScopeId == scopeId, TestContext.Current.CancellationToken)
        );

    public static Task<int> CountOwnedScopesAsync(ApiHost api, Guid accountId, string kind) =>
        api.QueryDatabaseAsync(context =>
            context
                .Set<SyncScope>()
                .CountAsync(scope => scope.OwnerAccountId == accountId && scope.Kind == kind, TestContext.Current.CancellationToken)
        );
}
