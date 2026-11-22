using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Extensions;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncChangeReader(
    MurmliDbContext context,
    SyncDataTypeRegistry registry,
    TimeProvider timeProvider,
    IOptions<SyncOptions> options
)
{
    public async Task<IReadOnlyList<SyncScopeHead>> ReadHeadsAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        await context
            .Set<SyncScope>()
            .Where(scope => scope.OwnerAccountId == accountId && scope.Kind == SyncScope.PersonalKind)
            .Select(scope => new SyncScopeHead(
                scope.Id,
                scope.Kind,
                scope.Version,
                scope.Epoch,
                scope.TombstoneFloor,
                context.Set<DatabaseIdentity>().Select(identity => identity.Value).First()
            ))
            .ToListAsync(cancellationToken);

    public bool IsUnchanged(SyncToken token, IReadOnlyList<SyncScopeHead> heads) =>
        token.Scopes.Count == heads.Count
        && heads.All(head =>
            head.DatabaseIdentity == token.DatabaseIdentity
            && token.Scopes.Contains(new SyncTokenScope(head.Id, head.Epoch, head.Version))
        );

    public Task<SyncOutcome> ReadAsync(
        Guid accountId,
        SyncToken? token,
        IReadOnlyList<SyncDataType> listed,
        IReadOnlyList<SyncCommandOutcome> outcomes,
        CancellationToken cancellationToken = default
    )
    {
        var stream = new MemoryStream();
        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                attemptCancellationToken => ReadSnapshotAsync(accountId, token, listed, outcomes, stream, attemptCancellationToken),
                cancellationToken
            );
    }

    private static long? GetKnownVersion(SyncScopeHead head, IReadOnlyList<SyncTokenScope> tokenScopes) =>
        FindTokenScope(tokenScopes, head.Id) is { } known
        && known.Epoch == head.Epoch
        && known.Version >= head.TombstoneFloor
        && known.Version <= head.Version
            ? known.Version
            : null;

    // A binary search instead of a dictionary, which colliding forged ids would flood; TryDecode guarantees the order
    private static SyncTokenScope? FindTokenScope(IReadOnlyList<SyncTokenScope> tokenScopes, Guid scopeId)
    {
        var lower = 0;
        var upper = tokenScopes.Count - 1;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            var comparison = BigEndianGuidComparer.Instance.Compare(tokenScopes[middle].ScopeId, scopeId);
            if (comparison == 0)
            {
                return tokenScopes[middle];
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return null;
    }

    // A merge instead of Distinct, which colliding forged ids would flood; equal ids end up adjacent
    private static List<Guid> GetRemovedScopeIds(
        IReadOnlyList<SyncTokenScope> tokenScopes,
        IReadOnlyList<SyncCommandOutcome> outcomes,
        Dictionary<Guid, long?> knownVersions
    )
    {
        var targetScopeIds = outcomes
            .Select(outcome => outcome.Target)
            .OfType<SyncCommandTarget>()
            .Select(target => target.ScopeId)
            .Order(BigEndianGuidComparer.Instance)
            .ToArray();
        var removedScopeIds = new List<Guid>();
        var tokenIndex = 0;
        var targetIndex = 0;
        while (tokenIndex < tokenScopes.Count || targetIndex < targetScopeIds.Length)
        {
            var takesToken =
                targetIndex == targetScopeIds.Length
                || (
                    tokenIndex < tokenScopes.Count
                    && BigEndianGuidComparer.Instance.Compare(tokenScopes[tokenIndex].ScopeId, targetScopeIds[targetIndex]) <= 0
                );
            var scopeId = takesToken ? tokenScopes[tokenIndex++].ScopeId : targetScopeIds[targetIndex++];
            if (!knownVersions.ContainsKey(scopeId) && (removedScopeIds.Count == 0 || removedScopeIds[^1] != scopeId))
            {
                removedScopeIds.Add(scopeId);
            }
        }

        return removedScopeIds;
    }

    private async Task<SyncOutcome> ReadSnapshotAsync(
        Guid accountId,
        SyncToken? token,
        IReadOnlyList<SyncDataType> listed,
        IReadOnlyList<SyncCommandOutcome> outcomes,
        MemoryStream stream,
        CancellationToken cancellationToken
    )
    {
        context.PrepareSyncUnitAttempt();
        stream.SetLength(0);
        using var response = new SyncResponseWriter(stream, options.Value);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var heads = await ReadHeadsAsync(accountId, cancellationToken);
        if (heads.Count == 0)
        {
            return new SyncOutcome.AccountGone();
        }

        var coversAllScopes = token is null || token.DatabaseIdentity != heads[0].DatabaseIdentity;
        var tokenScopes = coversAllScopes ? [] : token!.Scopes;
        var listedNames = listed.Select(dataType => dataType.Name).ToArray();
        var knownVersions = new Dictionary<Guid, long?>();
        foreach (var head in heads)
        {
            var knownVersion = GetKnownVersion(head, tokenScopes);
            if (knownVersion < head.Version && await HasTooManyDeletionsAsync(head, knownVersion.Value, listedNames, cancellationToken))
            {
                knownVersion = null;
            }

            knownVersions.Add(head.Id, knownVersion);
        }

        var includedTargets = heads.ToDictionary(head => head.Id, _ => new List<SyncCommandTarget>());
        foreach (var outcome in outcomes)
        {
            if (
                outcome.Status != SyncResultStatus.Applied
                && outcome.Target is { } target
                && knownVersions.TryGetValue(target.ScopeId, out var knownVersion)
                && knownVersion is not null
                && listedNames.Contains(target.DataType)
            )
            {
                includedTargets[target.ScopeId].Add(target);
            }
        }

        var removedScopeIds = GetRemovedScopeIds(token?.Scopes ?? [], outcomes, knownVersions);

        response.WriteStart(outcomes);
        foreach (var head in heads)
        {
            await WriteScopeAsync(response, head, knownVersions[head.Id], includedTargets[head.Id], listed, listedNames, cancellationToken);
        }

        var body = response.WriteEnd(removedScopeIds, coversAllScopes, heads, timeProvider.GetUtcNowInMilliseconds());
        await transaction.CommitAsync(cancellationToken);
        return new SyncOutcome.Completed(body);
    }

    private async Task<bool> HasTooManyDeletionsAsync(
        SyncScopeHead head,
        long knownVersion,
        string[] listedNames,
        CancellationToken cancellationToken
    )
    {
        var threshold = registry.GetDeletionThreshold(head.Kind);
        var count = await context
            .Set<SyncTombstone>()
            .Where(tombstone => tombstone.ScopeId == head.Id && tombstone.Version > knownVersion && listedNames.Contains(tombstone.DataType))
            .Take(int.CreateSaturating(threshold + 1L))
            .CountAsync(cancellationToken);
        return count > threshold;
    }

    private async Task WriteScopeAsync(
        SyncResponseWriter response,
        SyncScopeHead head,
        long? knownVersion,
        List<SyncCommandTarget> includedTargets,
        IReadOnlyList<SyncDataType> listed,
        string[] listedNames,
        CancellationToken cancellationToken
    )
    {
        if (knownVersion == head.Version && includedTargets.Count == 0)
        {
            return;
        }

        var afterVersion = knownVersion ?? 0;
        response.WriteScopeStart(head, fullState: knownVersion is null);
        var written = new HashSet<(string DataType, Guid Id)>();
        foreach (var dataType in listed.Where(dataType => dataType.ScopeKind == head.Kind))
        {
            var includedIds = includedTargets
                .Where(target => target.DataType == dataType.Name)
                .Select(target => target.RecordId)
                .Distinct()
                .ToArray();
            // No record has a version above its scope's, so without included ids nothing is left to read
            if (afterVersion == head.Version && includedIds.Length == 0)
            {
                continue;
            }

            var writtenIds = new HashSet<Guid>();
            await dataType.WriteRecordsAsync(context, head.Id, afterVersion, includedIds, response.Json, writtenIds, cancellationToken);
            written.UnionWith(writtenIds.Select(id => (dataType.Name, id)));
        }

        response.WriteDeletedRecordsStart();
        if (knownVersion < head.Version)
        {
            var tombstones = context
                .Set<SyncTombstone>()
                .Where(tombstone => tombstone.ScopeId == head.Id && tombstone.Version > afterVersion && listedNames.Contains(tombstone.DataType))
                .OrderBy(tombstone => tombstone.Version)
                .Select(tombstone => new { tombstone.DataType, tombstone.RecordId })
                .AsAsyncEnumerable();
            await foreach (var tombstone in tombstones.WithCancellation(cancellationToken))
            {
                response.WriteDeletedRecord(tombstone.DataType, tombstone.RecordId);
                written.Add((tombstone.DataType, tombstone.RecordId));
            }
        }

        foreach (var target in includedTargets)
        {
            if (written.Add((target.DataType, target.RecordId)))
            {
                response.WriteDeletedRecord(target.DataType, target.RecordId);
            }
        }

        response.WriteScopeEnd();
    }
}
