using System.ComponentModel.DataAnnotations;

namespace Murmli.Backend.Api.ShoppingLists;

internal sealed class ShoppingListOptions
{
    public const string SectionName = "ShoppingLists";

    [Range(typeof(TimeSpan), "00:01:00", "3650.00:00:00")]
    public TimeSpan CheckedItemRetention { get; set; } = TimeSpan.FromDays(30);
}
