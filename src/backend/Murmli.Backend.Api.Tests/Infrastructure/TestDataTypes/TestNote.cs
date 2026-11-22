using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestNote : SyncRecord
{
    public string Title { get; set; } = "";

    public DateTimeOffset TitleChangedAt { get; set; }

    public string? Color { get; set; }

    public decimal? Weight { get; set; }

    public DateTimeOffset StyleChangedAt { get; set; }

    public bool Pinned { get; set; }
}
