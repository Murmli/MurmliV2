using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Data.ShoppingLists;

public class ShoppingListItem : SyncRecord
{
    public string Name { get; set; } = "";

    public DateTimeOffset NameChangedAt { get; set; }

    public decimal? Quantity { get; set; }

    public string? Unit { get; set; }

    public DateTimeOffset AmountChangedAt { get; set; }

    public string Category { get; set; } = "";

    public DateTimeOffset CategoryChangedAt { get; set; }

    public bool Checked { get; set; }

    public DateTimeOffset CheckedChangedAt { get; set; }

    public string Language { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public Guid? ChangedByMemberId { get; set; }

    public Guid? CreatedByMemberId { get; set; }

    public DateTimeOffset? CheckedArrivedAt { get; set; }
}
