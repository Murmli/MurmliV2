using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Identity;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.Extensions;
using Murmli.Backend.Sync.Writing;
using Npgsql;

namespace Murmli.Backend.Sync.Accounts;

public sealed partial class SyncAccountLifecycle(
    MurmliDbContext context,
    SyncServerWriter writer,
    IEnumerable<ISyncAccountBootstrapContributor> bootstrapContributors,
    TimeProvider timeProvider,
    IOptions<SyncOptions> options,
    ILogger<SyncAccountLifecycle> logger
)
{
    private string? OwnerForeignKeyName =>
        context
            .Model.FindEntityType(typeof(SyncScope))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User))
            .GetConstraintName();

    public Task MarkDeletingAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNowInMilliseconds();
        return context
            .Database.CreateExecutionStrategy()
            .ExecuteInTransactionAsync(
                attemptCancellationToken => SetDeletionMarkerAsync(accountId, now, attemptCancellationToken),
                // A replay after an ambiguous commit finds the marker and changes nothing
                _ => Task.FromResult(false),
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
    }

    public async Task DeleteOwnedScopesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var scopeIds = await context
            .Set<SyncScope>()
            .Where(scope => scope.OwnerAccountId == accountId && scope.Kind != SyncScope.PersonalKind)
            .OrderBy(scope => scope.Id)
            .Select(scope => scope.Id)
            .ToListAsync(cancellationToken);
        var strategy = context.Database.CreateExecutionStrategy();
        foreach (var scopeId in scopeIds)
        {
            await strategy.ExecuteInTransactionAsync(
                attemptCancellationToken => DeleteOwnedScopeAsync(accountId, scopeId, attemptCancellationToken),
                // A replay after an ambiguous commit finds nothing left to delete
                _ => Task.FromResult(false),
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
        }
    }

    internal async Task<SyncBootstrapResult> EnsurePersonalScopeAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var personalScopeId = Guid.NewGuid();
        var epoch = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
        var now = timeProvider.GetUtcNowInMilliseconds();
        var bootstrapCommands = bootstrapContributors.SelectMany(contributor => contributor.CreateCommands(accountId)).ToList();
        try
        {
            var result = await context
                .Database.CreateExecutionStrategy()
                .ExecuteInTransactionAsync(
                    attemptCancellationToken =>
                        InsertPersonalScopeAsync(accountId, personalScopeId, epoch, now, bootstrapCommands, attemptCancellationToken),
                    // A replay after an ambiguous commit finds the scope and returns Existing
                    _ => Task.FromResult(false),
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                );
            if (result == SyncBootstrapResult.Created)
            {
                LogAccountBootstrapped(personalScopeId);
            }

            return result;
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation && exception.ConstraintName == OwnerForeignKeyName)
        {
            return SyncBootstrapResult.AccountGone;
        }
    }

    private async Task<SyncBootstrapResult> InsertPersonalScopeAsync(
        Guid accountId,
        Guid personalScopeId,
        int epoch,
        DateTimeOffset now,
        IReadOnlyList<SyncServerCommand> bootstrapCommands,
        CancellationToken cancellationToken
    )
    {
        context.PrepareSyncUnitAttempt();
        await SetLockTimeoutAsync(cancellationToken);
        // ON CONFLICT infers the partial unique index only from a literal predicate
        var inserted = await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO sync.scopes (id, kind, owner_account_id, version, epoch, tombstone_floor, created_at)
            SELECT {personalScopeId}, 'personal', u.id, 0, {epoch}, 0, {now}
            FROM identity.users u
            WHERE u.id = {accountId} AND u.deletion_started_at IS NULL
            ON CONFLICT (owner_account_id) WHERE kind = 'personal' DO NOTHING
            """,
            cancellationToken
        );
        if (inserted == 1)
        {
            foreach (var command in bootstrapCommands)
            {
                await writer.WriteInCurrentTransactionAsync(command, cancellationToken);
            }

            return SyncBootstrapResult.Created;
        }

        return await context
            .Set<SyncScope>()
            .AnyAsync(scope => scope.OwnerAccountId == accountId && scope.Kind == SyncScope.PersonalKind, cancellationToken)
            ? SyncBootstrapResult.Existing
            : SyncBootstrapResult.AccountGone;
    }

    private async Task SetDeletionMarkerAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        context.PrepareSyncUnitAttempt();
        await SetLockTimeoutAsync(cancellationToken);
        // Root creates read the marker after taking this lock, so none adds a scope once it is set
        await context.LockPersonalScopeAsync(accountId, cancellationToken);
        await context
            .Users.Where(user => user.Id == accountId && user.DeletionStartedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.DeletionStartedAt, now), cancellationToken);
    }

    private async Task DeleteOwnedScopeAsync(Guid accountId, Guid scopeId, CancellationToken cancellationToken)
    {
        context.PrepareSyncUnitAttempt();
        await SetLockTimeoutAsync(cancellationToken);
        await context
            .Set<SyncScope>()
            .Where(scope => scope.Id == scopeId && scope.OwnerAccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Task SetLockTimeoutAsync(CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {options.Value.GetLockTimeoutSetting()}, true)", cancellationToken);
}
