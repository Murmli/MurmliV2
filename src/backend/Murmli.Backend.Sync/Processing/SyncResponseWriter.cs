using System.Text.Json;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncResponseWriter(MemoryStream stream, SyncOptions options) : IDisposable
{
    public Utf8JsonWriter Json { get; } = new(stream);

    public void WriteStart(IReadOnlyList<SyncCommandOutcome> outcomes)
    {
        Json.WriteStartObject();
        Json.WriteStartArray("results");
        foreach (var outcome in outcomes)
        {
            WriteResult(outcome);
        }

        Json.WriteEndArray();
        Json.WriteStartArray("scopes");
    }

    public void WriteScopeStart(SyncScopeHead head, bool fullState)
    {
        Json.WriteStartObject();
        Json.WriteString("scopeId", head.Id);
        Json.WriteString("kind", head.Kind);
        Json.WriteBoolean("fullState", fullState);
        Json.WriteStartArray("records");
    }

    public void WriteDeletedRecordsStart()
    {
        Json.WriteEndArray();
        Json.WriteStartArray("deletedRecords");
    }

    public void WriteDeletedRecord(string dataType, Guid id)
    {
        Json.WriteStartObject();
        Json.WriteString("dataType", dataType);
        Json.WriteString("id", id);
        Json.WriteEndObject();
    }

    public void WriteScopeEnd()
    {
        Json.WriteEndArray();
        Json.WriteStartArray("members");
        Json.WriteEndArray();
        Json.WriteEndObject();
    }

    public ReadOnlyMemory<byte> WriteEnd(
        IReadOnlyList<Guid> removedScopeIds,
        bool coversAllScopes,
        IReadOnlyList<SyncScopeHead> heads,
        DateTimeOffset serverTime
    )
    {
        Json.WriteEndArray();
        Json.WriteStartArray("removedScopes");
        foreach (var scopeId in removedScopeIds)
        {
            Json.WriteStartObject();
            Json.WriteString("scopeId", scopeId);
            Json.WriteBoolean("removedByOwnAccount", false);
            Json.WriteEndObject();
        }

        Json.WriteEndArray();
        Json.WriteBoolean("coversAllScopes", coversAllScopes);
        Json.WriteString(
            "token",
            SyncTokenCodec.Encode(heads[0].DatabaseIdentity, heads.Select(head => new SyncTokenScope(head.Id, head.Epoch, head.Version)))
        );
        Json.WriteBoolean("hasMore", false);
        Json.WriteNumber("serverTime", serverTime.ToUnixTimeMilliseconds());
        Json.WriteStartObject("settings");
        Json.WriteNumber("pollIntervalSeconds", options.PollIntervalSeconds);
        Json.WriteNumber("maxCommandsPerRequest", options.MaxCommandsPerRequest);
        Json.WriteNumber("maxRequestBytes", options.MaxRequestBytes);
        Json.WriteEndObject();
        Json.WriteEndObject();
        Json.Flush();
        return new ReadOnlyMemory<byte>(stream.GetBuffer(), 0, (int)stream.Length);
    }

    public void Dispose() => Json.Dispose();

    private void WriteResult(SyncCommandOutcome outcome)
    {
        Json.WriteStartObject();
        if (outcome.CommandId is { } commandId)
        {
            Json.WriteString("commandId", commandId);
        }
        else
        {
            Json.WriteNull("commandId");
        }

        Json.WriteString("status", outcome.Status.Key);
        if (outcome.Status == SyncResultStatus.Rejected)
        {
            Json.WriteString("reason", outcome.Reason);
        }
        else if (outcome.Status == SyncResultStatus.Ignored)
        {
            Json.WriteString("cause", outcome.Cause);
        }
        else if (outcome.Status == SyncResultStatus.AlreadyProcessed && outcome.Original is { } original)
        {
            Json.WriteStartObject("original");
            Json.WriteString("status", original.Status);
            WriteStringIfPresent("reason", original.Reason);
            WriteStringIfPresent("cause", original.Cause);
            Json.WriteEndObject();
        }

        Json.WriteEndObject();
    }

    private void WriteStringIfPresent(string propertyName, string? value)
    {
        if (value is not null)
        {
            Json.WriteString(propertyName, value);
        }
    }
}
