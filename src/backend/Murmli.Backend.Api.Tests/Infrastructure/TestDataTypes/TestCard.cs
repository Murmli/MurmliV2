using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestCard : SyncRecord
{
    public string Text { get; set; } = "";

    public DateTimeOffset TextChangedAt { get; set; }

    public decimal? Count { get; set; }

    public string? Measure { get; set; }

    public DateTimeOffset AmountChangedAt { get; set; }

    public bool Done { get; set; }

    public DateTimeOffset DoneChangedAt { get; set; }

    public Guid? ChangedByMemberId { get; set; }

    public Guid? CreatedByMemberId { get; set; }

    public Guid? DoneByMemberId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
