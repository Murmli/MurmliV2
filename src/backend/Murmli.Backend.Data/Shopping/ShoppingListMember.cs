namespace Murmli.Backend.Data.Shopping;

public class ShoppingListMember
{
    public required Guid Id { get; set; }

    public required Guid ListId { get; set; }

    public required Guid UserId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
