using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Data.ShoppingLists;

public class ShoppingList : SyncRecord
{
    public string Name { get; set; } = "";

    public DateTimeOffset NameChangedAt { get; set; }
}
