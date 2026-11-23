using System.Text.Json.Nodes;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncCases;

internal sealed record SyncCaseRecord(string DataType, string Record, JsonObject Fields);
