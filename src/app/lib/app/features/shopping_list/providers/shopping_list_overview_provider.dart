import 'package:riverpod_annotation/riverpod_annotation.dart';

import '../models/market_category.dart';
import '../models/shopping_list.dart';
import '../models/shopping_list_group.dart';
import '../models/shopping_list_item.dart';
import '../models/shopping_list_overview.dart';
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
  final openItems = list.items.where((item) => !item.checked).toList();
  final checkedItems = list.items.where((item) => item.checked).toList()
    ..sort(_lastCheckedFirst);
  return ShoppingListOverview(
    listId: list.id,
    checkedCount: checkedItems.length,
    totalCount: list.items.length,
    groups: [
      for (final category in categoryOrder)
        if (openItems.where((item) => item.category == category).toList()
            case final items when items.isNotEmpty)
          ShoppingListGroup(category: category, items: items),
    ],
    checkedItems: checkedItems,
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
