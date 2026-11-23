using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Accounts;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Diagnostics;
using Murmli.Backend.Sync.Extensions;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

public sealed partial class SyncProcessor
{
    private const string CommandsPhase = "commands";

    private const string ChangesPhase = "changes";

    private const string BootstrapPhase = "bootstrap";

    // An unsigned token can name any number of unknown scopes, an honest one at most 85
    private const int MaxLoggedRemovedScopes = 100;

    private readonly SyncDataTypeRegistry registry;

    private readonly SyncCommandParser parser;

    private readonly SyncCommandUnit unit;

    private readonly SyncAccountLifecycle lifecycle;

    private readonly SyncChangeReader changeReader;

    private readonly TimeProvider timeProvider;

    private readonly SyncOptions options;

    private readonly SyncTelemetry telemetry;

    private readonly ILogger<SyncProcessor> logger;

    internal SyncProcessor(
        SyncDataTypeRegistry registry,
        SyncCommandParser parser,
        SyncCommandUnit unit,
        SyncAccountLifecycle lifecycle,
        SyncChangeReader changeReader,
        TimeProvider timeProvider,
        IOptions<SyncOptions> options,
        SyncTelemetry telemetry,
        ILogger<SyncProcessor> logger
    )
    {
        this.registry = registry;
        this.parser = parser;
        this.unit = unit;
        this.lifecycle = lifecycle;
        this.changeReader = changeReader;
        this.timeProvider = timeProvider;
        this.options = options.Value;
        this.telemetry = telemetry;
        this.logger = logger;
    }

    public async Task<SyncOutcome> ProcessAsync(
        SyncCaller caller,
        SyncRequest request,
        SyncResponseEncoding acceptedEncoding,
        CancellationToken cancellationToken = default
    )
    {
        telemetry.RecordRequest(caller.DeviceId, request);
        var listed = registry.GetListed(request.DataTypes);
        var token = SyncTokenCodec.TryDecode(request.Token, out var decoded) ? decoded : null;
        var phase = ChangesPhase;
        try
        {
            var heads = await changeReader.ReadHeadsAsync(caller.AccountId, cancellationToken);
            if (!SyncChangeReader.HasPersonalScope(heads))
            {
                phase = BootstrapPhase;
                if (await lifecycle.EnsurePersonalScopeAsync(caller.AccountId, cancellationToken) == SyncBootstrapResult.AccountGone)
                {
                    return new SyncOutcome.AccountGone();
                }

                phase = ChangesPhase;
                heads = await changeReader.ReadHeadsAsync(caller.AccountId, cancellationToken);
                if (!SyncChangeReader.HasPersonalScope(heads))
                {
                    return new SyncOutcome.AccountGone();
                }
            }

            if (SyncChangeReader.IsAccountBeingDeleted(heads))
            {
                return new SyncOutcome.AccountGone();
            }

            var visibleHeads = SyncChangeReader.GetVisibleHeads(heads, listed);
            if (request.Commands.Count == 0 && token is not null && changeReader.IsUnchanged(token, visibleHeads))
            {
                using var response = new SyncResponseWriter(new MemoryStream(), options, SyncResponseEncoding.Identity);
                response.WriteStart([]);
                var unchanged = response.WriteEnd([], false, visibleHeads, timeProvider.GetUtcNowInMilliseconds());
                RecordResponse(caller.DeviceId, 0, unchanged);
                return unchanged.Response;
            }

            phase = CommandsPhase;
            var outcomes = await ProcessCommandsAsync(caller, request.Commands, listed, cancellationToken);
            phase = ChangesPhase;
            var written = await changeReader.ReadAsync(
                caller.AccountId,
                token,
                request.Token is not null,
                listed,
                outcomes,
                acceptedEncoding,
                cancellationToken
            );
            if (written is null)
            {
                return new SyncOutcome.AccountGone();
            }

            RecordResponse(caller.DeviceId, outcomes.Count, written);
            return written.Response;
        }
        catch (Exception exception) when (exception.IsDatabaseDisturbance())
        {
            LogDatabaseUnavailable(exception, phase);
            return new SyncOutcome.DatabaseUnavailable();
        }
        catch (SyncAccountGoneException)
        {
            return new SyncOutcome.AccountGone();
        }
    }

    private static bool IsCommandFailure(Exception exception, CancellationToken cancellationToken) =>
        !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        && !exception.IsDatabaseDisturbance()
        && exception is not SyncAccountGoneException;

    private async Task<List<SyncCommandOutcome>> ProcessCommandsAsync(
        SyncCaller caller,
        IReadOnlyList<JsonElement> commands,
        IReadOnlyList<SyncDataType> listed,
        CancellationToken cancellationToken
    )
    {
        var outcomes = new List<SyncCommandOutcome>();
        var start = Stopwatch.GetTimestamp();
        foreach (var (index, command) in commands.Index())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index > 0 && Stopwatch.GetElapsedTime(start) >= options.CommandPhaseBudget)
            {
                break;
            }

            SyncParsedCommand? parsed = null;
            SyncCommandOutcome outcome;
            try
            {
                var now = timeProvider.GetUtcNowInMilliseconds();
                parsed = parser.Parse(command, listed);
                outcome = await ProcessCommandAsync(caller.AccountId, parsed, now, cancellationToken);
            }
            catch (Exception exception) when (IsCommandFailure(exception, cancellationToken))
            {
                LogCommandFailed(exception, parsed?.CommandId, parsed?.Kind?.Key, parsed?.DataType?.Name, caller.DeviceId);
                outcome = new SyncCommandOutcome(parsed?.CommandId, SyncResultStatus.Failed, null, null, null, parsed?.Target);
            }

            outcomes.Add(outcome);
            RecordCommand(caller.DeviceId, parsed, outcome);
            if (outcome.Status == SyncResultStatus.Failed)
            {
                break;
            }
        }

        return outcomes;
    }

    private async Task<SyncCommandOutcome> ProcessCommandAsync(
        Guid accountId,
        SyncParsedCommand parsed,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (parsed.Claim is not { } claim)
        {
            return new SyncCommandOutcome(
                parsed.CommandId,
                SyncResultStatus.Rejected,
                SyncRejectionReason.Invalid.Key,
                null,
                null,
                parsed.Target
            );
        }

        var preCheckRejection = parsed.Command is null ? SyncRejectionReason.Invalid : GetClockRejection(claim.Time, now);
        var command = preCheckRejection is null ? parsed.Command : null;
        return await unit.RunAsync(
            new SyncCommandUnitInput(accountId, claim, now, command, preCheckRejection, SyncScopeSeed.For(command)),
            cancellationToken
        );
    }

    private SyncRejectionReason? GetClockRejection(DateTimeOffset time, DateTimeOffset now) =>
        time < now - options.Retention ? SyncRejectionReason.TooOld
        : time > now + options.ClockAheadTolerance ? SyncRejectionReason.ClockAhead
        : null;

    private void RecordCommand(Guid deviceId, SyncParsedCommand? parsed, SyncCommandOutcome outcome)
    {
        var kind = parsed?.Kind?.Key;
        var dataType = parsed?.DataType?.Name;
        telemetry.RecordCommand(outcome.Status.Key, outcome.Reason ?? outcome.Cause, kind, dataType, SyncTelemetry.DeviceWriter);
        if (outcome.Status == SyncResultStatus.Rejected)
        {
            LogCommandRejected(deviceId, outcome.CommandId, kind, dataType, outcome.Target?.ScopeId, outcome.Target?.RecordId, outcome.Reason);
        }
    }

    private void RecordResponse(Guid deviceId, int processedCommandCount, SyncWrittenResponse written)
    {
        telemetry.RecordResponse(processedCommandCount, written);
        foreach (var removedScope in written.RemovedScopes.Take(MaxLoggedRemovedScopes))
        {
            LogScopeRemoved(deviceId, removedScope.ScopeId, removedScope.RemovedByOwnAccount);
        }
    }
}
