import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../models/market_category.dart';
import '../models/shopping_list.dart';
import '../models/shopping_list_group.dart';
import '../models/shopping_list_item.dart';
import '../models/shopping_list_overview.dart';
import 'leaving_shopping_list_items_provider.dart';
import 'shopping_list_repository.dart';

part 'shopping_list_overview_provider.g.dart';

@riverpod
Stream<ShoppingList?> currentShoppingList(Ref ref) =>
    ref.watch(shoppingListRepositoryProvider).watchList();

@riverpod
Stream<List<MarketCategory>> marketCategoryOrder(Ref ref) =>
    ref.watch(shoppingListRepositoryProvider).watchCategoryOrder();

@riverpod
ShoppingListOverview? shoppingListOverview(Ref ref) {
  final list = ref.watch(currentShoppingListProvider).value;
  final categoryOrder = ref.watch(marketCategoryOrderProvider).value;
  if (list == null || categoryOrder == null) {
    return null;
  }
  final leavingItems = ref.watch(leavingShoppingListItemsProvider);
  final openEntries = <ShoppingListItem>[];
  final checkedEntries = <ShoppingListItem>[];
  final leavingOpenItemIds = <String>{};
  final leavingCheckedItemIds = <String>{};
  for (final item in list.items) {
    (item.checked ? checkedEntries : openEntries).add(item);
    // Until the stored toggle arrives, the item itself still holds its old place.
    if (leavingItems[item.id] case final snapshot?
        when snapshot.checked != item.checked) {
      (snapshot.checked ? checkedEntries : openEntries).add(snapshot);
      (snapshot.checked ? leavingCheckedItemIds : leavingOpenItemIds).add(
        item.id,
      );
    }
  }
  checkedEntries.sort(_lastCheckedFirst);
  return ShoppingListOverview(
    listId: list.id,
    checkedCount: list.items.where((item) => item.checked).length,
    totalCount: list.items.length,
    groups: [
      for (final category in categoryOrder)
        if (openEntries.where((item) => item.category == category).toList()
            case final items when items.isNotEmpty)
          ShoppingListGroup(
            category: category,
            items: items,
            leavingItemIds: {
              for (final item in items)
                if (leavingOpenItemIds.contains(item.id)) item.id,
            },
          ),
    ],
    checkedItems: checkedEntries,
    leavingCheckedItemIds: leavingCheckedItemIds,
  );
}

int _lastCheckedFirst(ShoppingListItem first, ShoppingListItem second) {
  final firstChangedAt = first.changedAt;
  final secondChangedAt = second.changedAt;
  final byChangedAt = firstChangedAt == null || secondChangedAt == null
      ? 0
      : secondChangedAt.compareTo(firstChangedAt);
  return byChangedAt != 0 ? byChangedAt : first.id.compareTo(second.id);
}
