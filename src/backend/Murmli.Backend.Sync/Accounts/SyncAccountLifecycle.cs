using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Identity;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.Extensions;
using Npgsql;

namespace Murmli.Backend.Sync.Accounts;

internal sealed class SyncAccountLifecycle(MurmliDbContext context, TimeProvider timeProvider, IOptions<SyncOptions> options)
{
    private string? OwnerForeignKeyName =>
        context
            .Model.FindEntityType(typeof(SyncScope))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User))
            .GetConstraintName();

    public async Task<SyncBootstrapResult> EnsurePersonalScopeAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var personalScopeId = Guid.NewGuid();
        var epoch = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
        var now = timeProvider.GetUtcNowInMilliseconds();
        try
        {
            return await context
                .Database.CreateExecutionStrategy()
                .ExecuteInTransactionAsync(
                    attemptCancellationToken => InsertPersonalScopeAsync(accountId, personalScopeId, epoch, now, attemptCancellationToken),
                    // A replay after an ambiguous commit finds the scope and returns Existing
                    _ => Task.FromResult(false),
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                );
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
        CancellationToken cancellationToken
    )
    {
        context.PrepareSyncUnitAttempt();
        await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {options.Value.GetLockTimeoutSetting()}, true)", cancellationToken);
        // ON CONFLICT infers the partial unique index only from a literal predicate
        var inserted = await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO sync.scopes (id, kind, owner_account_id, version, epoch, tombstone_floor, created_at)
            SELECT {personalScopeId}, 'personal', u.id, 0, {epoch}, 0, {now}
            FROM identity.users u
            WHERE u.id = {accountId}
            ON CONFLICT (owner_account_id) WHERE kind = 'personal' DO NOTHING
            """,
            cancellationToken
        );
        if (inserted == 1)
        {
            return SyncBootstrapResult.Created;
        }

        return await context
            .Set<SyncScope>()
            .AnyAsync(scope => scope.OwnerAccountId == accountId && scope.Kind == SyncScope.PersonalKind, cancellationToken)
            ? SyncBootstrapResult.Existing
            : SyncBootstrapResult.AccountGone;
    }
}
