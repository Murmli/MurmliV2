using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class TestNoteQueries
{
    public static Task<int> CountInScopeAsync(ApiHost api, Guid scopeId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<TestNote>().CountAsync(note => note.ScopeId == scopeId, TestContext.Current.CancellationToken)
        );

    public static Task<string?> ReadTitleAsync(ApiHost api, Guid noteId) =>
        api.QueryDatabaseAsync(context =>
            context
                .Set<TestNote>()
                .Where(note => note.Id == noteId)
                .Select(note => note.Title)
                .SingleOrDefaultAsync(TestContext.Current.CancellationToken)
        );

    public static Task<List<ProcessedSyncCommand>> ReadProcessedCommandsAsync(ApiHost api, Guid accountId) =>
        api.QueryDatabaseAsync(context =>
            context
                .Set<ProcessedSyncCommand>()
                .AsNoTracking()
                .Where(command => command.AccountId == accountId && command.DataType != SyncTestDevice.ShoppingListDataType)
                .ToListAsync(TestContext.Current.CancellationToken)
        );
}
