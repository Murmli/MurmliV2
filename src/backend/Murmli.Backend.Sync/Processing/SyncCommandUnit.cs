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

    public async Task<SyncCommandOutcome> RunAsync(SyncCommandUnitInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            try
            {
                return await RunOnceAsync(input, cancellationToken);
            }
            catch (SyncCreateRaceException)
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
        new(claim.CommandId, status, reason, cause, null, new SyncCommandTarget(claim.ScopeId, claim.DataType, claim.RecordId));

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
        var processedCommand = new ProcessedSyncCommand
        {
            AccountId = input.AccountId,
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
        var (outcome, isNew) = await ApplyAsync(input.AccountId, command, input.Now, cancellationToken);
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
                && postgresException.ConstraintName == command.DataType.GetPrimaryKeyName(context))
        {
            throw new SyncCreateRaceException(exception);
        }

        return outcome;
    }

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
        var stored = await context
            .Set<ProcessedSyncCommand>()
            .Where(command => command.AccountId == processedCommand.AccountId && command.CommandId == processedCommand.CommandId)
            .Select(command => new
            {
                command.Status,
                command.Reason,
                command.Cause,
                command.ScopeId,
                command.DataType,
                command.RecordId,
            })
            .SingleAsync(cancellationToken);
        var status = stored.Status ?? throw new InvalidOperationException("A processed sync command has no stored result.");
        return new SyncCommandOutcome(
            processedCommand.CommandId,
            SyncResultStatus.AlreadyProcessed,
            null,
            null,
            new SyncStoredResult(status.Key, stored.Reason, stored.Cause),
            new SyncCommandTarget(stored.ScopeId, stored.DataType, stored.RecordId)
        );
    }

    private async Task<(SyncCommandOutcome Outcome, bool IsNew)> ApplyAsync(
        Guid accountId,
        SyncCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var claim = command.Claim;
        var scope = (
            await context
                .Set<SyncScope>()
                .FromSql($"SELECT * FROM sync.scopes WHERE id = {claim.ScopeId} FOR NO KEY UPDATE")
                .AsTracking()
                .ToListAsync(cancellationToken)
        ).SingleOrDefault();
        if (scope is null || scope.Kind != SyncScope.PersonalKind || scope.OwnerAccountId != accountId)
        {
            return (Rejected(claim, SyncRejectionReason.NoAccess), false);
        }

        if (command.DataType.ScopeKind != scope.Kind)
        {
            return (Rejected(claim, SyncRejectionReason.Invalid), false);
        }

        var record = await command.DataType.FindTrackedAsync(context, claim.RecordId, cancellationToken);
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

            if (await command.DataType.CountInScopeAsync(context, claim.ScopeId, cancellationToken) >= registry.GetMaxRecordsPerScope(command.DataType))
            {
                return (Rejected(claim, SyncRejectionReason.LimitExceeded), false);
            }

            record = command.DataType.CreateRecord(claim.RecordId, claim.ScopeId);
            isNew = true;
        }

        if (command.Kind != SyncCommandKind.Delete && !command.DataType.ApplyChanges(record, command.Changes!, claim.Time, isNew))
        {
            return (Ignored(claim, SyncIgnoreCause.LostToNewer), false);
        }

        if (GetRuleOutcome(claim, command.DataType.EvaluateRules(record)) is { } ruleOutcome)
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
                    DeletedAt = now,
                }
            );
            return (Applied(claim), false);
        }

        record.Version = version;
        if (isNew)
        {
            context.Add(record);
        }

        return (Applied(claim), isNew);
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
