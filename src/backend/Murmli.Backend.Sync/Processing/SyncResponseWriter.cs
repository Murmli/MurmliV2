using System.IO.Compression;
using System.Text.Json;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncResponseWriter : IDisposable
{
    private readonly MemoryStream stream;

    private readonly SyncOptions options;

    private readonly SyncResponseEncoding encoding;

    private readonly GZipStream? compression;

    private readonly List<string> fullStateReasons = [];

    public SyncResponseWriter(MemoryStream stream, SyncOptions options, SyncResponseEncoding encoding)
    {
        this.stream = stream;
        this.options = options;
        this.encoding = encoding;
        compression =
            encoding == SyncResponseEncoding.Gzip ? new GZipStream(stream, options.FullStateCompressionLevel, leaveOpen: true) : null;
        Json = new Utf8JsonWriter(compression ?? (Stream)stream);
    }

    public Utf8JsonWriter Json { get; }

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

    public void WriteScopeStart(SyncScopeHead head, string? fullStateReason)
    {
        Json.WriteStartObject();
        Json.WriteString("scopeId", head.Id);
        Json.WriteString("kind", head.Kind);
        Json.WriteBoolean("fullState", fullStateReason is not null);
        if (fullStateReason is not null)
        {
            fullStateReasons.Add(fullStateReason);
        }

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

    public void WriteScopeEnd(IReadOnlyList<SyncMember> members)
    {
        Json.WriteEndArray();
        Json.WriteStartArray("members");
        foreach (var member in members)
        {
            Json.WriteStartObject();
            Json.WriteString("memberId", member.MemberId);
            Json.WriteString("name", member.Name);
            Json.WriteString("role", member.Role);
            Json.WriteString("state", member.State);
            Json.WriteBoolean("isOwn", member.IsOwn);
            Json.WriteEndObject();
        }

        Json.WriteEndArray();
        Json.WriteEndObject();
        // With gzip the writer holds at most one uncompressed block; without gzip, a flush per block would let the response buffer grow to twice the body
        if (compression is not null)
        {
            Json.Flush();
        }
    }

    public SyncWrittenResponse WriteEnd(
        IReadOnlyList<SyncRemovedScope> removedScopes,
        bool coversAllScopes,
        IReadOnlyList<SyncScopeHead> heads,
        DateTimeOffset serverTime
    )
    {
        Json.WriteEndArray();
        Json.WriteStartArray("removedScopes");
        foreach (var removedScope in removedScopes)
        {
            Json.WriteStartObject();
            Json.WriteString("scopeId", removedScope.ScopeId);
            Json.WriteBoolean("removedByOwnAccount", removedScope.RemovedByOwnAccount);
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
        var uncompressedLength = Json.BytesCommitted + Json.BytesPending;
        Dispose();
        return new SyncWrittenResponse(
            new SyncOutcome.Completed(new ReadOnlyMemory<byte>(stream.GetBuffer(), 0, (int)stream.Length), encoding),
            uncompressedLength,
            fullStateReasons,
            removedScopes
        );
    }

    // The writer first: its disposal flushes into the gzip stream, whose disposal then writes the gzip trailer
    public void Dispose()
    {
        Json.Dispose();
        compression?.Dispose();
    }

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
