namespace Murmli.Backend.Data.Shopping;

public class ShoppingList
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required DateTimeOffset NameChangedAt { get; set; }

    public long Version { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
