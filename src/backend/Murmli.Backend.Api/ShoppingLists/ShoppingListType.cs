using Murmli.Backend.Data.ShoppingLists;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.ShoppingLists;

internal sealed class ShoppingListType : SyncDataType<ShoppingList>
{
    public const string DataTypeName = "shoppingList";

    public const string ScopeKindName = "shoppingList";

    protected override void Define(SyncDataTypeBuilder<ShoppingList> type)
    {
        type.Named(DataTypeName, ScopeKindName).ScopeRoot(maximumOwnedScopesPerAccount: 42).MaximumRecordsPerScope(1);
        type.Field("name", list => list.Name, list => list.NameChangedAt, SyncFieldTypes.Text(0, 100)).RequiredOnCreate();
    }
}
