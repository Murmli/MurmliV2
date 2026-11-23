using System.Text.Json;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Diagnostics;
using Murmli.Backend.Sync.Extensions;
using Murmli.Backend.Sync.Processing;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Writing;

public sealed partial class SyncServerWriter
{
    private readonly SyncDataTypeRegistry registry;

    private readonly SyncCommandUnit unit;

    private readonly MurmliDbContext context;

    private readonly TimeProvider timeProvider;

    private readonly SyncTelemetry telemetry;

    private readonly ILogger<SyncServerWriter> logger;

    internal SyncServerWriter(
        SyncDataTypeRegistry registry,
        SyncCommandUnit unit,
        MurmliDbContext context,
        TimeProvider timeProvider,
        SyncTelemetry telemetry,
        ILogger<SyncServerWriter> logger
    )
    {
        this.registry = registry;
        this.unit = unit;
        this.context = context;
        this.timeProvider = timeProvider;
        this.telemetry = telemetry;
        this.logger = logger;
    }

    public async Task<SyncServerWriteResult> WriteAsync(SyncServerCommand command, CancellationToken cancellationToken = default)
    {
        if (CreateInput(command) is not { } input)
        {
            RecordCommand(command, SyncResultStatus.Rejected, SyncRejectionReason.Invalid.Key);
            return new SyncServerWriteResult(SyncResultStatus.Rejected.Key, SyncRejectionReason.Invalid.Key, null);
        }

        try
        {
            var outcome = await unit.RunAsync(input, cancellationToken);
            RecordCommand(command, outcome.Status, outcome.Reason ?? outcome.Cause);
            return outcome.Original is { } original
                ? new SyncServerWriteResult(original.Status, original.Reason, original.Cause)
                : new SyncServerWriteResult(outcome.Status.Key, outcome.Reason, outcome.Cause);
        }
        catch (SyncAccountGoneException)
        {
            RecordCommand(command, SyncResultStatus.Ignored, SyncIgnoreCause.TargetDeleted.Key);
            return new SyncServerWriteResult(SyncResultStatus.Ignored.Key, null, SyncIgnoreCause.TargetDeleted.Key);
        }
        catch (Exception exception)
            when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested) && !exception.IsDatabaseDisturbance())
        {
            LogCommandFailed(exception, command.CommandId, input.Claim.Kind, input.Claim.DataType);
            RecordCommand(command, SyncResultStatus.Failed, null);
            return new SyncServerWriteResult(SyncResultStatus.Failed.Key, null, null);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    internal async Task WriteInCurrentTransactionAsync(SyncServerCommand command, CancellationToken cancellationToken = default)
    {
        var input =
            CreateInput(command)
            ?? throw new InvalidOperationException($"The bootstrap command for the data type {command.DataType} is invalid.");
        await unit.RunInCurrentTransactionAsync(input, cancellationToken);
    }

    private void RecordCommand(SyncServerCommand command, SyncResultStatus status, string? reasonOrCause) =>
        telemetry.RecordCommand(
            status.Key,
            reasonOrCause,
            SyncCommandKind.TryParse(command.Kind.Key, out var kind) ? kind.Key : null,
            registry.TryGet(command.DataType, out var dataType) ? dataType.Name : null,
            SyncTelemetry.ServerWriter
        );

    private SyncCommandUnitInput? CreateInput(SyncServerCommand command)
    {
        Guid? accountId;
        switch (command.Actor)
        {
            case SyncActor.AccountActor account:
                accountId = account.AccountId;
                break;
            case SyncActor.SystemActor:
                accountId = null;
                break;
            default:
                return null;
        }

        if (
            !registry.TryGet(command.DataType, out var dataType)
            || !SyncCommandKind.TryParse(command.Kind.Key, out var kind)
            || (dataType.IsScopeRoot ? command.RecordId != command.ScopeId : command.RecordId == command.ScopeId)
            || (accountId is null && dataType.IsScopeRoot && (kind == SyncCommandKind.Create || kind == SyncCommandKind.Delete))
            || (command.Precondition is not null && dataType.IsScopeRoot && kind == SyncCommandKind.Delete)
            || command.ScopeId.Version != 4
            || command.RecordId.Version != 4
        )
        {
            return null;
        }

        var changes =
            kind == SyncCommandKind.Delete || command.Fields is null
                ? null
                : dataType.ParseFields(JsonSerializer.SerializeToElement(command.Fields), kind, isServerWrite: true);
        if (kind == SyncCommandKind.Delete ? command.Fields is not null : changes is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNowInMilliseconds();
        var claim = new SyncCommandClaim(command.CommandId, kind.Key, command.ScopeId, dataType.Name, command.RecordId, now);
        var syncCommand = new SyncCommand(claim, kind, dataType, changes);
        return new SyncCommandUnitInput(
            accountId,
            claim,
            now,
            syncCommand,
            PreCheckRejection: null,
            SyncScopeSeed.For(syncCommand),
            IsServerWrite: true,
            command.Precondition
        );
    }
}
