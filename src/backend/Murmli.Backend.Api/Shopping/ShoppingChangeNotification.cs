namespace Murmli.Backend.Api.Shopping;

internal sealed record ShoppingChangeNotification(IReadOnlyCollection<Guid> UserIds)
{
    public const string Channel = "shopping_changes";
}
