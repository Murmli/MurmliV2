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
    public static bool HasPersonalScope(IReadOnlyList<SyncScopeHead> heads) => heads.Count > 0 && heads[0].Kind == SyncScope.PersonalKind;

    public static IReadOnlyList<SyncScopeHead> GetVisibleHeads(IReadOnlyList<SyncScopeHead> heads, IReadOnlyList<SyncDataType> listed)
    {
        var visibleKinds = listed.Where(dataType => dataType.IsScopeRoot).Select(dataType => dataType.ScopeKind).ToArray();
        return [.. heads.Where(head => head.Kind == SyncScope.PersonalKind || visibleKinds.Contains(head.Kind, StringComparer.Ordinal))];
    }

    public async Task<IReadOnlyList<SyncScopeHead>> ReadHeadsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var personal = context
            .Set<SyncScope>()
            .Where(scope => scope.OwnerAccountId == accountId && scope.Kind == SyncScope.PersonalKind)
            .Select(scope => new { scope.Id, scope.Kind, scope.Version, scope.Epoch, scope.TombstoneFloor });
        var shared = context
            .Set<SyncMemberEntry>()
            .Where(entry => entry.AccountId == accountId && entry.State == SyncMemberState.Active)
            .Join(
                context.Set<SyncScope>(),
                entry => entry.ScopeId,
                scope => scope.Id,
                (entry, scope) => new { scope.Id, scope.Kind, scope.Version, scope.Epoch, scope.TombstoneFloor }
            );
        var heads = await personal
            .Concat(shared)
            .Select(head => new SyncScopeHead(
                head.Id,
                head.Kind,
                head.Version,
                head.Epoch,
                head.TombstoneFloor,
                context.Set<DatabaseIdentity>().Select(identity => identity.Value).First()
            ))
            .ToListAsync(cancellationToken);
        return [.. heads.OrderBy(head => head.Kind != SyncScope.PersonalKind).ThenBy(head => head.Id, BigEndianGuidComparer.Instance)];
    }

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
        SyncResponseEncoding acceptedEncoding,
        CancellationToken cancellationToken = default
    )
    {
        var stream = new MemoryStream();
        return context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                attemptCancellationToken =>
                    ReadSnapshotAsync(accountId, token, listed, outcomes, acceptedEncoding, stream, attemptCancellationToken),
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
        IReadOnlySet<Guid> presentScopeIds
    )
    {
        var targetScopeIds = outcomes
            // The device keeps and resends failed and accepted commands, so their target scopes must stay on it
            .Where(outcome => outcome.Status != SyncResultStatus.Failed && outcome.Status != SyncResultStatus.Accepted)
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
            if (!presentScopeIds.Contains(scopeId) && (removedScopeIds.Count == 0 || removedScopeIds[^1] != scopeId))
            {
                removedScopeIds.Add(scopeId);
            }
        }

        return removedScopeIds;
    }

    private static bool IsRootDeleteNotApplied(SyncScopeHead head, IReadOnlyList<SyncCommandOutcome> outcomes) =>
        outcomes.Any(outcome =>
            outcome.Status != SyncResultStatus.Applied && outcome.Target is { IsRootDelete: true } target && target.ScopeId == head.Id
        );

    private async Task<SyncOutcome> ReadSnapshotAsync(
        Guid accountId,
        SyncToken? token,
        IReadOnlyList<SyncDataType> listed,
        IReadOnlyList<SyncCommandOutcome> outcomes,
        SyncResponseEncoding acceptedEncoding,
        MemoryStream stream,
        CancellationToken cancellationToken
    )
    {
        context.PrepareSyncUnitAttempt();
        stream.SetLength(0);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var heads = await ReadHeadsAsync(accountId, cancellationToken);
        if (!HasPersonalScope(heads))
        {
            return new SyncOutcome.AccountGone();
        }

        var visibleHeads = GetVisibleHeads(heads, listed);
        var coversAllScopes = token is null || token.DatabaseIdentity != heads[0].DatabaseIdentity;
        var tokenScopes = coversAllScopes ? [] : token!.Scopes;
        var listedNames = listed.Select(dataType => dataType.Name).ToArray();
        var knownVersions = new Dictionary<Guid, long?>();
        foreach (var head in visibleHeads)
        {
            var knownVersion = IsRootDeleteNotApplied(head, outcomes) ? null : GetKnownVersion(head, tokenScopes);
            if (knownVersion < head.Version && await HasTooManyDeletionsAsync(head, knownVersion.Value, listedNames, cancellationToken))
            {
                knownVersion = null;
            }

            knownVersions.Add(head.Id, knownVersion);
        }

        var includedTargets = visibleHeads.ToDictionary(head => head.Id, _ => new List<SyncCommandTarget>());
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

        var removedScopeIds = GetRemovedScopeIds(token?.Scopes ?? [], outcomes, heads.Select(head => head.Id).ToHashSet());
        var ownRootDeleteScopeIds = removedScopeIds.Count == 0 ? [] : await ReadOwnRootDeleteScopeIdsAsync(accountId, removedScopeIds, cancellationToken);
        IReadOnlyList<SyncRemovedScope> removedScopes =
        [
            .. removedScopeIds.Select(scopeId => new SyncRemovedScope(
                scopeId,
                Array.BinarySearch(ownRootDeleteScopeIds, scopeId, BigEndianGuidComparer.Instance) >= 0
            )),
        ];

        // Responses without a full state stay uncompressed against BREACH (docs/sync.md "Kompression")
        using var response = new SyncResponseWriter(
            stream,
            options.Value,
            acceptedEncoding == SyncResponseEncoding.Gzip && knownVersions.ContainsValue(null) ? SyncResponseEncoding.Gzip : SyncResponseEncoding.Identity
        );

        response.WriteStart(outcomes);
        foreach (var head in visibleHeads)
        {
            await WriteScopeAsync(
                response,
                accountId,
                head,
                knownVersions[head.Id],
                includedTargets[head.Id],
                listed,
                listedNames,
                cancellationToken
            );
        }

        var completed = response.WriteEnd(removedScopes, coversAllScopes, visibleHeads, timeProvider.GetUtcNowInMilliseconds());
        await transaction.CommitAsync(cancellationToken);
        return completed;
    }

    private async Task<Guid[]> ReadOwnRootDeleteScopeIdsAsync(
        Guid accountId,
        List<Guid> removedScopeIds,
        CancellationToken cancellationToken
    )
    {
        var scopeIds = removedScopeIds.ToArray();
        var ownRootDeletes = await context
            .Set<ProcessedSyncCommand>()
            .Where(command =>
                command.AccountId == accountId
                // Literals, so the partial index ix_processed_commands_root_deletes applies
                && command.Kind == EF.Constant(SyncCommandKind.Delete.Key)
                && command.Status == SyncResultStatus.Applied
                && command.RecordId == command.ScopeId
                && scopeIds.Contains(command.ScopeId)
            )
            .Select(command => command.ScopeId)
            .ToListAsync(cancellationToken);
        return ownRootDeletes.Order(BigEndianGuidComparer.Instance).ToArray();
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
        Guid accountId,
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

        response.WriteScopeEnd(head.Kind == SyncScope.PersonalKind ? [] : await ReadMembersAsync(head.Id, accountId, cancellationToken));
    }

    private async Task<IReadOnlyList<SyncMember>> ReadMembersAsync(Guid scopeId, Guid accountId, CancellationToken cancellationToken)
    {
        var rows = await context
            .Set<SyncMemberEntry>()
            .Where(entry => entry.ScopeId == scopeId)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => new
            {
                entry.Id,
                entry.Name,
                entry.Role,
                entry.State,
                IsOwn = entry.AccountId == accountId,
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SyncMember(row.Id, row.Name, row.Role, row.State.Key, row.IsOwn))];
    }
}
