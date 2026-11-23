using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncCases;

internal sealed record SyncCaseResult(string Status, string? Reason = null, string? Cause = null, SyncOriginalResult? Original = null);
