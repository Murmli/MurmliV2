using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Accounts;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Extensions;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

public sealed partial class SyncProcessor
{
    private const string CommandsPhase = "commands";

    private const string ChangesPhase = "changes";

    private const string BootstrapPhase = "bootstrap";

    private readonly SyncDataTypeRegistry registry;

    private readonly SyncCommandParser parser;

    private readonly SyncCommandUnit unit;

    private readonly SyncAccountLifecycle lifecycle;

    private readonly SyncChangeReader changeReader;

    private readonly TimeProvider timeProvider;

    private readonly SyncOptions options;

    private readonly ILogger<SyncProcessor> logger;

    internal SyncProcessor(
        SyncDataTypeRegistry registry,
        SyncCommandParser parser,
        SyncCommandUnit unit,
        SyncAccountLifecycle lifecycle,
        SyncChangeReader changeReader,
        TimeProvider timeProvider,
        IOptions<SyncOptions> options,
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
        this.logger = logger;
    }

    public async Task<SyncOutcome> ProcessAsync(
        SyncCaller caller,
        SyncRequest request,
        SyncResponseEncoding acceptedEncoding,
        CancellationToken cancellationToken = default
    )
    {
        var listed = registry.GetListed(request.DataTypes);
        var token = SyncTokenCodec.TryDecode(request.Token, out var decoded) ? decoded : null;
        var phase = ChangesPhase;
        try
        {
            var heads = await changeReader.ReadHeadsAsync(caller.AccountId, cancellationToken);
            if (heads.Count == 0)
            {
                phase = BootstrapPhase;
                if (await lifecycle.EnsurePersonalScopeAsync(caller.AccountId, cancellationToken) == SyncBootstrapResult.AccountGone)
                {
                    return new SyncOutcome.AccountGone();
                }

                phase = ChangesPhase;
                heads = await changeReader.ReadHeadsAsync(caller.AccountId, cancellationToken);
                if (heads.Count == 0)
                {
                    return new SyncOutcome.AccountGone();
                }
            }

            if (request.Commands.Count == 0 && token is not null && changeReader.IsUnchanged(token, heads))
            {
                using var response = new SyncResponseWriter(new MemoryStream(), options, SyncResponseEncoding.Identity);
                response.WriteStart([]);
                return response.WriteEnd([], false, heads, timeProvider.GetUtcNowInMilliseconds());
            }

            phase = CommandsPhase;
            var outcomes = await ProcessCommandsAsync(caller, request.Commands, listed, cancellationToken);
            phase = ChangesPhase;
            return await changeReader.ReadAsync(caller.AccountId, token, listed, outcomes, acceptedEncoding, cancellationToken);
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

    private static SyncScopeSeed? CreateScopeSeed(SyncCommand? command) =>
        command is { DataType.IsScopeRoot: true } && command.Kind == SyncCommandKind.Create
            ? new SyncScopeSeed(Guid.NewGuid(), RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue))
            : null;

    private static string? GetKnownKind(SyncCommandClaim? claim) =>
        claim is not null && SyncCommandKind.TryParse(claim.Kind, out var kind) ? kind.Key : null;

    private string? GetRegisteredDataType(SyncCommandClaim? claim) =>
        claim is not null && registry.TryGet(claim.DataType, out var dataType) ? dataType.Name : null;

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
            try
            {
                var now = timeProvider.GetUtcNowInMilliseconds();
                parsed = parser.Parse(command, listed);
                outcomes.Add(await ProcessCommandAsync(caller.AccountId, parsed, now, cancellationToken));
            }
            catch (Exception exception) when (IsCommandFailure(exception, cancellationToken))
            {
                LogCommandFailed(exception, parsed?.CommandId, GetKnownKind(parsed?.Claim), GetRegisteredDataType(parsed?.Claim), caller.DeviceId);
                outcomes.Add(new SyncCommandOutcome(parsed?.CommandId, SyncResultStatus.Failed, null, null, null, parsed?.Target));
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
            new SyncCommandUnitInput(accountId, claim, now, command, preCheckRejection, CreateScopeSeed(command)),
            cancellationToken
        );
    }

    private SyncRejectionReason? GetClockRejection(DateTimeOffset time, DateTimeOffset now) =>
        time < now - options.Retention ? SyncRejectionReason.TooOld
        : time > now + options.ClockAheadTolerance ? SyncRejectionReason.ClockAhead
        : null;
}
