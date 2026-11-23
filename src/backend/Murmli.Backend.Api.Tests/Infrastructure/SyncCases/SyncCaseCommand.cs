using System.Text.Json.Nodes;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncCases;

internal sealed record SyncCaseCommand(
    string Device,
    string Kind,
    string DataType,
    string Scope,
    string Record,
    long At,
    JsonObject? Fields = null,
    string? Label = null
);
