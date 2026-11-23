using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Extensions;
using Murmli.Backend.Sync.Protocol;
using Npgsql;

namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncCommandUnit(MurmliDbContext context, SyncDataTypeRegistry registry, IOptions<SyncOptions> options)
{
    private readonly string lockTimeout = options.Value.GetLockTimeoutSetting();

    private string? AccountForeignKeyName =>
        context.Model.FindEntityType(typeof(ProcessedSyncCommand))!.GetForeignKeys().Single().GetConstraintName();

    private string? ScopePrimaryKeyName => context.Model.FindEntityType(typeof(SyncScope))!.FindPrimaryKey()!.GetName();

    public async Task<SyncCommandOutcome> RunAsync(SyncCommandUnitInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            try
            {
                return await RunOnceAsync(input, cancellationToken);
            }
            catch (Exception exception) when (exception is SyncCreateRaceException or SyncClaimRaceException)
            {
                return await RunOnceAsync(input, cancellationToken);
            }
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation && exception.ConstraintName == AccountForeignKeyName)
        {
            throw new SyncAccountGoneException(exception);
        }
    }

    private static SyncCommandOutcome Applied(SyncCommandClaim claim) => CreateOutcome(claim, SyncResultStatus.Applied, null, null);

    private static SyncCommandOutcome Ignored(SyncCommandClaim claim, SyncIgnoreCause cause) =>
        CreateOutcome(claim, SyncResultStatus.Ignored, null, cause.Key);

    private static SyncCommandOutcome Rejected(SyncCommandClaim claim, SyncRejectionReason reason) =>
        CreateOutcome(claim, SyncResultStatus.Rejected, reason.Key, null);

    private static SyncCommandOutcome CreateOutcome(SyncCommandClaim claim, SyncResultStatus status, string? reason, string? cause) =>
        new(
            claim.CommandId,
            status,
            reason,
            cause,
            null,
            SyncCommandTarget.FromCommand(claim.ScopeId, claim.DataType, claim.RecordId, claim.Kind)
        );

    private static SyncCommandOutcome? GetRuleOutcome(SyncCommandClaim claim, SyncRuleDecision decision) =>
        decision switch
        {
            { RejectionReason: { } reason } => Rejected(claim, reason),
            { IgnoreCause: { } cause } => Ignored(claim, cause),
            _ => null,
        };

    private Task<SyncCommandOutcome> RunOnceAsync(SyncCommandUnitInput input, CancellationToken cancellationToken) =>
        context
            .Database.CreateExecutionStrategy()
            .ExecuteInTransactionAsync(
                input,
                RunAttemptAsync,
                // A replay after an ambiguous commit finds the claim and returns the stored result
                (_, _) => Task.FromResult(false),
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

    private async Task<SyncCommandOutcome> RunAttemptAsync(SyncCommandUnitInput input, CancellationToken cancellationToken)
    {
        context.PrepareSyncUnitAttempt();
        var claim = input.Claim;
        var accountId = input.AccountId ?? await ReadScopeOwnerAsync(claim.ScopeId, cancellationToken);
        if (accountId is null)
        {
            return Ignored(claim, SyncIgnoreCause.TargetDeleted);
        }

        var processedCommand = new ProcessedSyncCommand
        {
            AccountId = accountId.Value,
            CommandId = claim.CommandId,
            Kind = claim.Kind,
            ScopeId = claim.ScopeId,
            DataType = claim.DataType,
            RecordId = claim.RecordId,
            ProcessedAt = input.Now,
            RetainFrom = claim.Time < input.Now ? claim.Time : input.Now,
        };
        if (input.PreCheckRejection is { } rejection)
        {
            return await ClaimRejectedAsync(processedCommand, rejection, cancellationToken)
                ? Rejected(claim, rejection)
                : await ReadAlreadyProcessedAsync(processedCommand, cancellationToken);
        }

        if (!await ClaimAsync(processedCommand, cancellationToken))
        {
            return await ReadAlreadyProcessedAsync(processedCommand, cancellationToken);
        }

        var command = input.Command!;
        var (outcome, isNew) = await ApplyAsync(input, accountId.Value, cancellationToken);
        if (outcome.Status != SyncResultStatus.Applied)
        {
            context.ChangeTracker.Clear();
        }

        StoreResult(processedCommand, outcome);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (isNew
                && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgresException
                && (
                    postgresException.ConstraintName == command.DataType.GetPrimaryKeyName(context)
                    || postgresException.ConstraintName == ScopePrimaryKeyName
                ))
        {
            throw new SyncCreateRaceException(exception);
        }

        return outcome;
    }

    private Task<Guid?> ReadScopeOwnerAsync(Guid scopeId, CancellationToken cancellationToken) =>
        context
            .Set<SyncScope>()
            .Where(scope => scope.Id == scopeId)
            .Select(scope => (Guid?)scope.OwnerAccountId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> ClaimAsync(ProcessedSyncCommand processedCommand, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO sync.processed_commands (account_id, command_id, kind, scope_id, data_type, record_id, processed_at, retain_from)
            SELECT {processedCommand.AccountId}, {processedCommand.CommandId}, {processedCommand.Kind}, {processedCommand.ScopeId},
                {processedCommand.DataType}, {processedCommand.RecordId}, {processedCommand.ProcessedAt}, {processedCommand.RetainFrom}
            FROM (SELECT set_config('lock_timeout', {lockTimeout}, true)) AS settings
            ON CONFLICT (account_id, command_id) DO NOTHING
            """,
            cancellationToken
        ) == 1;

    private async Task<bool> ClaimRejectedAsync(
        ProcessedSyncCommand processedCommand,
        SyncRejectionReason reason,
        CancellationToken cancellationToken
    ) =>
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO sync.processed_commands (account_id, command_id, kind, scope_id, data_type, record_id, processed_at, retain_from, status, reason)
            SELECT {processedCommand.AccountId}, {processedCommand.CommandId}, {processedCommand.Kind}, {processedCommand.ScopeId},
                {processedCommand.DataType}, {processedCommand.RecordId}, {processedCommand.ProcessedAt}, {processedCommand.RetainFrom},
                {SyncResultStatus.Rejected.Key}, {reason.Key}
            FROM (SELECT set_config('lock_timeout', {lockTimeout}, true)) AS settings
            ON CONFLICT (account_id, command_id) DO NOTHING
            """,
            cancellationToken
        ) == 1;

    private async Task<SyncCommandOutcome> ReadAlreadyProcessedAsync(ProcessedSyncCommand processedCommand, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING locks no row, so the cleanup or an account deletion can remove it before this read
        var stored = await context
            .Set<ProcessedSyncCommand>()
            .Where(command => command.AccountId == processedCommand.AccountId && command.CommandId == processedCommand.CommandId)
            .Select(command => new
            {
                command.Status,
                command.Reason,
                command.Cause,
                command.Kind,
                command.ScopeId,
                command.DataType,
                command.RecordId,
            })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new SyncClaimRaceException();
        var status = stored.Status ?? throw new InvalidOperationException("A processed sync command has no stored result.");
        return new SyncCommandOutcome(
            processedCommand.CommandId,
            SyncResultStatus.AlreadyProcessed,
            null,
            null,
            new SyncStoredResult(status.Key, stored.Reason, stored.Cause),
            SyncCommandTarget.FromCommand(stored.ScopeId, stored.DataType, stored.RecordId, stored.Kind)
        );
    }

    private async Task<(SyncCommandOutcome Outcome, bool IsNew)> ApplyAsync(
        SyncCommandUnitInput input,
        Guid accountId,
        CancellationToken cancellationToken
    )
    {
        var command = input.Command!;
        var claim = command.Claim;
        var dataType = command.DataType;
        var isSystemWrite = input.IsServerWrite && input.AccountId is null;
        var isRootCreate = dataType.IsScopeRoot && command.Kind == SyncCommandKind.Create;
        int? ownedScopeCount = null;
        if (isRootCreate)
        {
            if (await context.LockPersonalScopeAsync(accountId, cancellationToken) is null)
            {
                throw new SyncAccountGoneException();
            }

            var account = await context
                .Users.Where(user => user.Id == accountId)
                .Select(user => new
                {
                    IsBeingDeleted = user.DeletionStartedAt != null,
                    OwnedScopeCount = context
                        .Set<SyncScope>()
                        .Count(scope => scope.OwnerAccountId == accountId && scope.Kind == dataType.ScopeKind),
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (account is not { IsBeingDeleted: false })
            {
                throw new SyncAccountGoneException();
            }

            ownedScopeCount = account.OwnedScopeCount;
        }

        var scope = (
            await context
                .Set<SyncScope>()
                .FromSql($"SELECT * FROM sync.scopes WHERE id = {claim.ScopeId} FOR NO KEY UPDATE")
                .AsTracking()
                .ToListAsync(cancellationToken)
        ).SingleOrDefault();
        if (scope is null)
        {
            return ownedScopeCount is { } count ? CreateScope(input, accountId, count)
                : isSystemWrite ? (Ignored(claim, SyncIgnoreCause.TargetDeleted), false)
                : (Rejected(claim, SyncRejectionReason.NoAccess), false);
        }

        SyncMemberRole? memberRole = null;
        Guid? actingMemberId = null;
        if (input.IsServerWrite)
        {
            if (scope.Kind != SyncScope.PersonalKind && input.AccountId is { } onBehalfOfAccountId)
            {
                actingMemberId = await context
                    .Set<SyncMemberEntry>()
                    .Where(memberEntry => memberEntry.ScopeId == scope.Id && memberEntry.AccountId == onBehalfOfAccountId)
                    .Select(memberEntry => (Guid?)memberEntry.Id)
                    .SingleOrDefaultAsync(cancellationToken);
            }
        }
        else if (scope.Kind == SyncScope.PersonalKind)
        {
            if (scope.OwnerAccountId != accountId)
            {
                return (Rejected(claim, SyncRejectionReason.NoAccess), false);
            }
        }
        else
        {
            var entry = await context
                .Set<SyncMemberEntry>()
                .Where(memberEntry => memberEntry.ScopeId == scope.Id && memberEntry.AccountId == accountId)
                .Select(memberEntry => new
                {
                    memberEntry.Id,
                    memberEntry.Role,
                    memberEntry.State,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (entry is null || entry.State != SyncMemberState.Active)
            {
                return (Rejected(claim, SyncRejectionReason.NoAccess), false);
            }

            memberRole = new SyncMemberRole(entry.Role);
            actingMemberId = entry.Id;
        }

        if (dataType.ScopeKind != scope.Kind)
        {
            return (Rejected(claim, SyncRejectionReason.Invalid), false);
        }

        var isRootDelete = dataType.IsScopeRoot && command.Kind == SyncCommandKind.Delete;
        if (memberRole is { } role && !role.Satisfies(isRootDelete ? SyncMemberRole.Owner : dataType.MinimumRole(command.Kind)))
        {
            return (Rejected(claim, SyncRejectionReason.NoAccess), false);
        }

        if (input.IsServerWrite && (isRootCreate || isRootDelete) && scope.OwnerAccountId != accountId)
        {
            return (Rejected(claim, SyncRejectionReason.NoAccess), false);
        }

        if (isRootDelete)
        {
            // One statement: a tracked member entry would make EF Core delete it before the scope and fail on the person references
            await context.Set<SyncScope>().Where(target => target.Id == scope.Id).ExecuteDeleteAsync(cancellationToken);
            return (Applied(claim), false);
        }

        var record = await dataType.FindTrackedAsync(context, claim.RecordId, cancellationToken);
        if (record is not null && record.ScopeId != claim.ScopeId)
        {
            return (Rejected(claim, SyncRejectionReason.NoAccess), false);
        }

        var isNew = false;
        if (record is null)
        {
            var tombstoneScopeId = await context
                .Set<SyncTombstone>()
                .Where(tombstone => tombstone.DataType == claim.DataType && tombstone.RecordId == claim.RecordId)
                .Select(tombstone => (Guid?)tombstone.ScopeId)
                .SingleOrDefaultAsync(cancellationToken);
            if (tombstoneScopeId is not null && tombstoneScopeId != claim.ScopeId)
            {
                return (Rejected(claim, SyncRejectionReason.NoAccess), false);
            }

            if (tombstoneScopeId is not null || command.Kind != SyncCommandKind.Create)
            {
                return (Ignored(claim, SyncIgnoreCause.TargetDeleted), false);
            }

            if (await dataType.CountInScopeAsync(context, claim.ScopeId, cancellationToken) >= registry.GetMaximumRecordsPerScope(dataType))
            {
                return (Rejected(claim, SyncRejectionReason.LimitExceeded), false);
            }

            record = dataType.CreateRecord(claim.RecordId, claim.ScopeId);
            isNew = true;
        }

        if (!isNew && input.Precondition is { } precondition && !precondition(record))
        {
            return (Ignored(claim, SyncIgnoreCause.LostToNewer), false);
        }

        if (command.Kind != SyncCommandKind.Delete && !dataType.ApplyChanges(record, command.Changes!, claim.Time, isNew))
        {
            return (Ignored(claim, SyncIgnoreCause.LostToNewer), false);
        }

        if (GetRuleOutcome(claim, dataType.EvaluateRules(record, actingMemberId)) is { } ruleOutcome)
        {
            return (ruleOutcome, false);
        }

        var version = scope.Version + 1;
        scope.Version = version;
        if (command.Kind == SyncCommandKind.Delete)
        {
            context.Remove(record);
            context.Add(
                new SyncTombstone
                {
                    DataType = claim.DataType,
                    RecordId = claim.RecordId,
                    ScopeId = claim.ScopeId,
                    Version = version,
                    DeletedAt = input.Now,
                }
            );
            return (Applied(claim), false);
        }

        dataType.ApplyFills(record, isNew, claim.Time, actingMemberId, isSystemWrite);
        record.Version = version;
        if (isNew)
        {
            context.Add(record);
        }

        return (Applied(claim), isNew);
    }

    private (SyncCommandOutcome Outcome, bool IsNew) CreateScope(SyncCommandUnitInput input, Guid accountId, int ownedScopeCount)
    {
        var command = input.Command!;
        var claim = command.Claim;
        var dataType = command.DataType;
        if (ownedScopeCount >= registry.GetMaximumOwnedScopesPerAccount(dataType))
        {
            return (Rejected(claim, SyncRejectionReason.LimitExceeded), false);
        }

        var seed = input.ScopeSeed!;
        var record = dataType.CreateRecord(claim.RecordId, claim.ScopeId);
        dataType.ApplyChanges(record, command.Changes!, claim.Time, isNew: true);
        if (GetRuleOutcome(claim, dataType.EvaluateRules(record, seed.OwnerMemberId)) is { } ruleOutcome)
        {
            return (ruleOutcome, false);
        }

        dataType.ApplyFills(record, isNew: true, claim.Time, seed.OwnerMemberId, isSystemWrite: false);
        record.Version = 1;
        context.Add(
            new SyncScope
            {
                Id = claim.ScopeId,
                Kind = dataType.ScopeKind,
                OwnerAccountId = accountId,
                Version = 1,
                Epoch = seed.Epoch,
                TombstoneFloor = 0,
                CreatedAt = input.Now,
            }
        );
        context.Add(
            new SyncMemberEntry
            {
                Id = seed.OwnerMemberId,
                ScopeId = claim.ScopeId,
                AccountId = accountId,
                Name = "",
                Role = SyncMemberRole.Owner.Key,
                State = SyncMemberState.Active,
                CreatedAt = input.Now,
            }
        );
        context.Add(record);
        return (Applied(claim), true);
    }

    private void StoreResult(ProcessedSyncCommand processedCommand, SyncCommandOutcome outcome)
    {
        var entry = context.Attach(processedCommand);
        processedCommand.Status = outcome.Status;
        processedCommand.Reason = outcome.Reason;
        processedCommand.Cause = outcome.Cause;
        entry.Property(command => command.Status).IsModified = true;
        entry.Property(command => command.Reason).IsModified = true;
        entry.Property(command => command.Cause).IsModified = true;
    }
}
