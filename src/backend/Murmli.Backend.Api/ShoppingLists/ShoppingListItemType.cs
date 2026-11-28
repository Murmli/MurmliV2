using System.Text.RegularExpressions;
using Murmli.Backend.Data.ShoppingLists;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.ShoppingLists;

internal sealed partial class ShoppingListItemType : SyncDataType<ShoppingListItem>
{
    public const string DataTypeName = "shoppingListItem";

    protected override void Define(SyncDataTypeBuilder<ShoppingListItem> type)
    {
        type.Named(DataTypeName, ShoppingListType.ScopeKindName).MaximumRecordsPerScope(1_000);
        type.Field("name", item => item.Name, item => item.NameChangedAt, SyncFieldTypes.Text(1, 256)).RequiredOnCreate();
        type.Group(
            "amount",
            item => item.AmountChangedAt,
            amount =>
            {
                amount.Field("quantity", item => item.Quantity, SyncFieldTypes.Decimal(0m, 999_999.999m, maximumScale: 3));
                amount.Field("unit", item => item.Unit, SyncFieldTypes.NullableKey(ShoppingCatalog.Units));
            }
        );
        type.Field("category", item => item.Category, item => item.CategoryChangedAt, SyncFieldTypes.Key(ShoppingCatalog.Categories))
            .RequiredOnCreate();
        type.Field("checked", item => item.Checked, item => item.CheckedChangedAt, SyncFieldTypes.Boolean());
        type.CreateOnly("language", item => item.Language, SyncFieldTypes.Text(2, 3, LanguagePattern())).RequiredOnCreate();
        type.ServerField("createdAt", item => item.CreatedAt, SyncFieldTypes.Timestamp(), SyncServerFill.CommandTimeOnCreate);
        type.ServerField("changedAt", item => item.ChangedAt, SyncFieldTypes.Timestamp(), SyncServerFill.CommandTimeOnApply);
        type.ServerField("changedBy", item => item.ChangedByMemberId, SyncFieldTypes.MemberReference(), SyncServerFill.ActingMemberOnApply);
        type.ServerField(
            "createdBy",
            item => item.CreatedByMemberId,
            SyncFieldTypes.MemberReference(),
            SyncServerFill.ActingMemberOnCreate,
            isSentToDevices: false
        );
    }

    protected override SyncRuleDecision Evaluate(SyncRuleContext<ShoppingListItem> context)
    {
        var item = context.Record;
        if (context.Applied(record => record.Checked) && item.Checked != context.Original(record => record.Checked))
        {
            item.CheckedArrivedAt = item.Checked ? context.ServerTime : null;
        }

        return SyncRuleDecision.Continue;
    }

    [GeneratedRegex(@"\A[a-z]{2,3}\z")]
    private static partial Regex LanguagePattern();
}
