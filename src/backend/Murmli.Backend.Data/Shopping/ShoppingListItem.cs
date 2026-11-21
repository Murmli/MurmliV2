namespace Murmli.Backend.Data.Shopping;

public class ShoppingListItem
{
    public required Guid Id { get; set; }

    public required Guid ListId { get; set; }

    public required string Name { get; set; }

    public double? Quantity { get; set; }

    public string? Unit { get; set; }

    public string? Category { get; set; }

    public required bool Checked { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
